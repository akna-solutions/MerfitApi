using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Translations;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminTranslationService'in varsayilan implementasyonu.</summary>
public class AdminTranslationService : IAdminTranslationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminTranslationService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminTranslationDto>> GetListAsync(AdminTranslationListRequest request, CancellationToken cancellationToken = default)
    {
        var translations = _unitOfWork.Repository<Translation>().GetQueryable();
        var languages = _unitOfWork.Repository<Language>().GetQueryable();

        var query = translations.AsQueryable();

        if (request.LanguageId.HasValue) query = query.Where(x => x.LanguageId == request.LanguageId.Value);
        if (!string.IsNullOrWhiteSpace(request.ResourceKey)) query = query.Where(x => x.ResourceKey.Contains(request.ResourceKey));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.ResourceKey.Contains(term) || x.Value.Contains(term));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        var page = await query
            .OrderBy(x => x.ResourceKey)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new AdminTranslationDto
            {
                Id = x.Id,
                LanguageId = x.LanguageId,
                LanguageCode = languages.Where(l => l.Id == x.LanguageId).Select(l => l.Code).FirstOrDefault() ?? "-",
                ResourceKey = x.ResourceKey,
                Value = x.Value,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return PagedResult<AdminTranslationDto>.Create(page, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminTranslationDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await MapToDtoAsync(await GetOrThrowAsync(id, cancellationToken), cancellationToken);

    public async Task<AdminTranslationDto> CreateAsync(AdminUpsertTranslationRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateLanguageAsync(request.LanguageId, cancellationToken);

        var repo = _unitOfWork.Repository<Translation>();
        if (await repo.AnyAsync(x => x.LanguageId == request.LanguageId && x.ResourceKey == request.ResourceKey, cancellationToken))
        {
            throw new ConflictException("Bu dil icin bu ResourceKey zaten kayitli. Guncellemek icin PUT kullanin.");
        }

        var entity = new Translation
        {
            LanguageId = request.LanguageId,
            ResourceKey = request.ResourceKey.Trim(),
            Value = request.Value,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await repo.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "TRANSLATION_CREATED", nameof(Translation), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return await MapToDtoAsync(entity, cancellationToken);
    }

    public async Task<AdminTranslationDto> UpdateAsync(long id, AdminUpsertTranslationRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateLanguageAsync(request.LanguageId, cancellationToken);

        var repo = _unitOfWork.Repository<Translation>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Translation), id);

        if (await repo.AnyAsync(x => x.LanguageId == request.LanguageId && x.ResourceKey == request.ResourceKey && x.Id != id, cancellationToken))
        {
            throw new ConflictException("Bu dil icin bu ResourceKey zaten baska bir kayitta kullaniliyor.");
        }

        var oldValue = new { entity.LanguageId, entity.ResourceKey, entity.Value };

        entity.LanguageId = request.LanguageId;
        entity.ResourceKey = request.ResourceKey.Trim();
        entity.Value = request.Value;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "TRANSLATION_UPDATED", nameof(Translation), id, ipAddress, userAgent, oldValue, new { entity.LanguageId, entity.ResourceKey, entity.Value }, cancellationToken);

        return await MapToDtoAsync(entity, cancellationToken);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Translation>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Translation), id);

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "TRANSLATION_DELETED", nameof(Translation), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    public async Task<AdminTranslationBulkResultDto> ImportAsync(AdminImportTranslationsRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateLanguageAsync(request.LanguageId, cancellationToken);

        var keys = request.Items.Select(x => x.ResourceKey).ToList();
        if (keys.Count != keys.Distinct().Count())
        {
            throw new AppValidationException(nameof(request.Items), "Ayni ResourceKey listede birden fazla kez yer alamaz.");
        }

        var repo = _unitOfWork.Repository<Translation>();

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var existing = await repo.GetQueryable(asNoTracking: false)
            .Where(x => x.LanguageId == request.LanguageId && keys.Contains(x.ResourceKey))
            .ToListAsync(cancellationToken);

        var createdCount = 0;
        var updatedCount = 0;
        var now = DateTime.UtcNow;

        foreach (var item in request.Items)
        {
            var match = existing.FirstOrDefault(x => x.ResourceKey == item.ResourceKey);
            if (match is null)
            {
                await repo.AddAsync(new Translation
                {
                    LanguageId = request.LanguageId,
                    ResourceKey = item.ResourceKey.Trim(),
                    Value = item.Value,
                    CreatedAt = now,
                    CreatedUser = adminUserId,
                }, cancellationToken);
                createdCount++;
            }
            else
            {
                match.Value = item.Value;
                match.UpdatedAt = now;
                match.UpdatedUser = adminUserId;
                repo.Update(match);
                updatedCount++;
            }
        }

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "TRANSLATION_IMPORTED", nameof(Translation), request.LanguageId, ipAddress, userAgent,
            newValue: new { request.LanguageId, CreatedCount = createdCount, UpdatedCount = updatedCount }, cancellationToken: cancellationToken);

        return new AdminTranslationBulkResultDto { CreatedCount = createdCount, UpdatedCount = updatedCount };
    }

    public async Task<AdminTranslationBulkResultDto> BulkUpdateAsync(AdminBulkUpdateTranslationsRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var languageIds = request.Items.Select(x => x.LanguageId).Distinct().ToList();
        var existingLanguageCount = await _unitOfWork.Repository<Language>().GetQueryable()
            .CountAsync(x => languageIds.Contains(x.Id), cancellationToken);

        if (existingLanguageCount != languageIds.Count)
        {
            throw new AppValidationException("items", "Listede bulunmayan bir LanguageId var.");
        }

        var repo = _unitOfWork.Repository<Translation>();

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var createdCount = 0;
        var updatedCount = 0;
        var now = DateTime.UtcNow;

        foreach (var languageGroup in request.Items.GroupBy(x => x.LanguageId))
        {
            var keys = languageGroup.Select(x => x.ResourceKey).ToList();

            var existing = await repo.GetQueryable(asNoTracking: false)
                .Where(x => x.LanguageId == languageGroup.Key && keys.Contains(x.ResourceKey))
                .ToListAsync(cancellationToken);

            foreach (var item in languageGroup)
            {
                var match = existing.FirstOrDefault(x => x.ResourceKey == item.ResourceKey);
                if (match is null)
                {
                    await repo.AddAsync(new Translation
                    {
                        LanguageId = item.LanguageId,
                        ResourceKey = item.ResourceKey.Trim(),
                        Value = item.Value,
                        CreatedAt = now,
                        CreatedUser = adminUserId,
                    }, cancellationToken);
                    createdCount++;
                }
                else
                {
                    match.Value = item.Value;
                    match.UpdatedAt = now;
                    match.UpdatedUser = adminUserId;
                    repo.Update(match);
                    updatedCount++;
                }
            }
        }

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "TRANSLATION_BULK_UPDATED", nameof(Translation), null, ipAddress, userAgent,
            newValue: new { CreatedCount = createdCount, UpdatedCount = updatedCount }, cancellationToken: cancellationToken);

        return new AdminTranslationBulkResultDto { CreatedCount = createdCount, UpdatedCount = updatedCount };
    }

    private async Task ValidateLanguageAsync(long languageId, CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.Repository<Language>().AnyAsync(x => x.Id == languageId, cancellationToken);
        if (!exists)
        {
            throw new AppValidationException("languageId", "Belirtilen dil bulunamadi.");
        }
    }

    private async Task<Translation> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Translation>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Translation), id);
    }

    private async Task<AdminTranslationDto> MapToDtoAsync(Translation entity, CancellationToken cancellationToken)
    {
        var languageCode = await _unitOfWork.Repository<Language>().GetQueryable()
            .Where(l => l.Id == entity.LanguageId).Select(l => l.Code).FirstOrDefaultAsync(cancellationToken);

        return new AdminTranslationDto
        {
            Id = entity.Id,
            LanguageId = entity.LanguageId,
            LanguageCode = languageCode ?? "-",
            ResourceKey = entity.ResourceKey,
            Value = entity.Value,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }
}
