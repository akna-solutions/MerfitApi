using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Legal;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminLegalService'in varsayilan implementasyonu.</summary>
public class AdminLegalService : IAdminLegalService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminLegalService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    // ---------- Legal Documents ----------

    public async Task<PagedResult<AdminLegalDocumentListItemDto>> GetDocumentsAsync(AdminLegalDocumentListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<LegalDocument>().GetQueryable();

        if (request.Type.HasValue) query = query.Where(x => x.Type == request.Type.Value);
        if (!string.IsNullOrWhiteSpace(request.Language)) query = query.Where(x => x.Language == request.Language);
        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Title.Contains(term) || x.Version.Contains(term));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        var raw = await query
            .OrderByDescending(x => x.PublishedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = raw.Select(MapToListItem).ToList();

        return PagedResult<AdminLegalDocumentListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminLegalDocumentDetailDto> GetDocumentByIdAsync(long id, CancellationToken cancellationToken = default)
        => MapToDetail(await GetOrThrowAsync(id, cancellationToken));

    public async Task<AdminLegalDocumentDetailDto> CreateDocumentAsync(AdminUpsertLegalDocumentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<LegalDocument>();

        if (await repo.AnyAsync(x => x.Type == request.Type && x.Language == request.Language && x.Version == request.Version, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Version), "Bu tur/dil/surum kombinasyonu icin bir belge zaten mevcut.");
        }

        var entity = new LegalDocument
        {
            Type = request.Type,
            Version = request.Version.Trim(),
            Language = request.Language.Trim().ToLowerInvariant(),
            Title = request.Title.Trim(),
            Content = request.Content,
            PublishedAt = request.PublishedAt ?? DateTime.UtcNow,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await repo.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LEGAL_DOCUMENT_CREATED", nameof(LegalDocument), entity.Id, ipAddress, userAgent,
            newValue: new { entity.Type, entity.Version, entity.Language, entity.IsActive }, cancellationToken: cancellationToken);

        return MapToDetail(entity);
    }

    public async Task<AdminLegalDocumentDetailDto> UpdateDocumentAsync(long id, AdminUpsertLegalDocumentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<LegalDocument>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LegalDocument), id);

        if (await repo.AnyAsync(x => x.Type == request.Type && x.Language == request.Language && x.Version == request.Version && x.Id != id, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Version), "Bu tur/dil/surum kombinasyonu icin baska bir belge zaten mevcut.");
        }

        var oldValue = new { entity.Type, entity.Version, entity.Language, entity.IsActive };

        entity.Type = request.Type;
        entity.Version = request.Version.Trim();
        entity.Language = request.Language.Trim().ToLowerInvariant();
        entity.Title = request.Title.Trim();
        entity.Content = request.Content;
        entity.PublishedAt = request.PublishedAt ?? entity.PublishedAt;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LEGAL_DOCUMENT_UPDATED", nameof(LegalDocument), id, ipAddress, userAgent,
            oldValue, new { entity.Type, entity.Version, entity.Language, entity.IsActive }, cancellationToken);

        return MapToDetail(entity);
    }

    public async Task DeleteDocumentAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<LegalDocument>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(LegalDocument), id);

        var hasConsents = await _unitOfWork.Repository<UserConsent>().AnyAsync(x => x.DocumentId == id, cancellationToken);
        if (hasConsents)
        {
            throw new ConflictException("Bu belgeye kullanicilar tarafindan onay verildigi (UserConsent) icin silinemez.");
        }

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LEGAL_DOCUMENT_DELETED", nameof(LegalDocument), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    public async Task<AdminLegalDocumentDetailDto> PublishDocumentAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<LegalDocument>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LegalDocument), id);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // Ayni tur+dile ait diger tum belgeler pasif hale getirilir; her an icin en fazla bir
        // "yururlukte" (IsActive) surum bulunur.
        var siblings = await repo.GetQueryable(asNoTracking: false)
            .Where(x => x.Type == entity.Type && x.Language == entity.Language && x.Id != id && x.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var sibling in siblings)
        {
            sibling.IsActive = false;
            sibling.UpdatedAt = DateTime.UtcNow;
            sibling.UpdatedUser = adminUserId;
            repo.Update(sibling);
        }

        entity.IsActive = true;
        entity.PublishedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;
        repo.Update(entity);

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LEGAL_DOCUMENT_PUBLISHED", nameof(LegalDocument), id, ipAddress, userAgent,
            newValue: new { entity.Type, entity.Version, entity.Language, DeactivatedCount = siblings.Count }, cancellationToken: cancellationToken);

        return MapToDetail(entity);
    }

    // ---------- User Consents (read-only) ----------

    public async Task<PagedResult<AdminConsentListItemDto>> GetUserConsentsAsync(AdminUserConsentListRequest request, CancellationToken cancellationToken = default)
    {
        var consents = _unitOfWork.Repository<UserConsent>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();
        var documents = _unitOfWork.Repository<LegalDocument>().GetQueryable();

        if (request.UserId.HasValue) consents = consents.Where(x => x.UserId == request.UserId.Value);
        if (request.DocumentId.HasValue) consents = consents.Where(x => x.DocumentId == request.DocumentId.Value);
        if (request.Accepted.HasValue) consents = consents.Where(x => x.Accepted == request.Accepted.Value);

        var totalCount = await consents.LongCountAsync(cancellationToken);

        var raw = await consents
            .OrderByDescending(x => x.AcceptedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.DocumentId,
                x.Accepted,
                x.AcceptedAt,
                x.Version,
                x.IpAddress,
                UserEmail = users.Where(u => u.Id == x.UserId).Select(u => u.Email).FirstOrDefault(),
                DocumentTitle = documents.Where(d => d.Id == x.DocumentId).Select(d => d.Title).FirstOrDefault(),
                DocumentType = documents.Where(d => d.Id == x.DocumentId).Select(d => d.Type).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = raw.Select(x => new AdminConsentListItemDto
        {
            Id = x.Id,
            UserId = x.UserId,
            UserEmail = x.UserEmail ?? "-",
            DocumentId = x.DocumentId,
            DocumentTitle = x.DocumentTitle ?? "-",
            DocumentType = x.DocumentType.ToString(),
            Accepted = x.Accepted,
            AcceptedAt = x.AcceptedAt,
            Version = x.Version,
            IpAddress = x.IpAddress,
        }).ToList();

        return PagedResult<AdminConsentListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    private async Task<LegalDocument> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<LegalDocument>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(LegalDocument), id);
    }

    private static AdminLegalDocumentListItemDto MapToListItem(LegalDocument entity) => new()
    {
        Id = entity.Id,
        Type = entity.Type.ToString(),
        Version = entity.Version,
        Language = entity.Language,
        Title = entity.Title,
        PublishedAt = entity.PublishedAt,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
    };

    private static AdminLegalDocumentDetailDto MapToDetail(LegalDocument entity) => new()
    {
        Id = entity.Id,
        Type = entity.Type.ToString(),
        Version = entity.Version,
        Language = entity.Language,
        Title = entity.Title,
        PublishedAt = entity.PublishedAt,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
        Content = entity.Content,
        UpdatedAt = entity.UpdatedAt,
    };
}