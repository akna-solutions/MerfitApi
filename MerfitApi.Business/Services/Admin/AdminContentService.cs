using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Content;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminContentService'in varsayilan implementasyonu.</summary>
public class AdminContentService : IAdminContentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminContentService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminContentDto>> GetListAsync(AdminContentListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<AppContent>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Key.Contains(term) || (x.Title != null && x.Title.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(request.Type)) query = query.Where(x => x.Type == request.Type);
        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);

        var raw = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = raw.Select(MapToDto).ToList();

        return PagedResult<AdminContentDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminContentDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => MapToDto(await GetOrThrowAsync(id, cancellationToken));

    public async Task<AdminContentDto> CreateAsync(AdminUpsertContentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        ValidateDateRange(request);

        var repo = _unitOfWork.Repository<AppContent>();
        if (await repo.AnyAsync(x => x.Key == request.Key, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Key), "Bu key zaten kullaniliyor.");
        }

        var entity = new AppContent
        {
            Key = request.Key.Trim().ToLowerInvariant(),
            Type = request.Type.Trim(),
            Title = request.Title,
            Description = request.Description,
            ImageUrl = request.ImageUrl,
            LinkUrl = request.LinkUrl,
            IsActive = request.IsActive,
            StartAt = request.StartAt,
            EndAt = request.EndAt,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await repo.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "CONTENT_CREATED", nameof(AppContent), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return MapToDto(entity);
    }

    public async Task<AdminContentDto> UpdateAsync(long id, AdminUpsertContentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        ValidateDateRange(request);

        var repo = _unitOfWork.Repository<AppContent>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(AppContent), id);

        if (await repo.AnyAsync(x => x.Key == request.Key && x.Id != id, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Key), "Bu key zaten kullaniliyor.");
        }

        var oldValue = new { entity.Key, entity.Type, entity.IsActive, entity.StartAt, entity.EndAt };

        entity.Key = request.Key.Trim().ToLowerInvariant();
        entity.Type = request.Type.Trim();
        entity.Title = request.Title;
        entity.Description = request.Description;
        entity.ImageUrl = request.ImageUrl;
        entity.LinkUrl = request.LinkUrl;
        entity.IsActive = request.IsActive;
        entity.StartAt = request.StartAt;
        entity.EndAt = request.EndAt;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "CONTENT_UPDATED", nameof(AppContent), id, ipAddress, userAgent,
            oldValue, new { entity.Key, entity.Type, entity.IsActive, entity.StartAt, entity.EndAt }, cancellationToken);

        return MapToDto(entity);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<AppContent>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(AppContent), id);

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "CONTENT_DELETED", nameof(AppContent), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateContentStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<AppContent>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(AppContent), id);

        var oldValue = new { entity.IsActive };
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "CONTENT_STATUS_CHANGED", nameof(AppContent), id, ipAddress, userAgent, oldValue, new { entity.IsActive }, cancellationToken);
    }

    public async Task<AdminContentDto> PublishAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<AppContent>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(AppContent), id);

        var oldValue = new { entity.IsActive, entity.StartAt };

        entity.IsActive = true;
        entity.StartAt ??= DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "CONTENT_PUBLISHED", nameof(AppContent), id, ipAddress, userAgent,
            oldValue, new { entity.IsActive, entity.StartAt }, cancellationToken);

        return MapToDto(entity);
    }

    private static void ValidateDateRange(AdminUpsertContentRequest request)
    {
        if (request.StartAt.HasValue && request.EndAt.HasValue && request.EndAt.Value <= request.StartAt.Value)
        {
            throw new AppValidationException(nameof(request.EndAt), "Bitis tarihi baslangic tarihinden sonra olmalidir.");
        }
    }

    private async Task<AppContent> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<AppContent>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(AppContent), id);
    }

    private static AdminContentDto MapToDto(AppContent entity)
    {
        var now = DateTime.UtcNow;
        var isCurrentlyLive = entity.IsActive
            && (entity.StartAt == null || entity.StartAt <= now)
            && (entity.EndAt == null || entity.EndAt >= now);

        return new AdminContentDto
        {
            Id = entity.Id,
            Key = entity.Key,
            Type = entity.Type,
            Title = entity.Title,
            Description = entity.Description,
            ImageUrl = entity.ImageUrl,
            LinkUrl = entity.LinkUrl,
            IsActive = entity.IsActive,
            StartAt = entity.StartAt,
            EndAt = entity.EndAt,
            IsCurrentlyLive = isCurrentlyLive,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }
}
