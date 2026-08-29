using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Languages;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminLanguageService'in varsayilan implementasyonu.</summary>
public class AdminLanguageService : IAdminLanguageService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminLanguageService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminLanguageDto>> GetListAsync(AdminLanguageListRequest request, CancellationToken cancellationToken = default)
    {
        var languages = _unitOfWork.Repository<Language>().GetQueryable();
        var translations = _unitOfWork.Repository<Translation>().GetQueryable();

        var query = languages.AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term));
        }

        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);

        var page = await query
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new AdminLanguageDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                IsDefault = x.IsDefault,
                IsActive = x.IsActive,
                TranslationCount = translations.Count(t => t.LanguageId == x.Id),
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return PagedResult<AdminLanguageDto>.Create(page, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminLanguageDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);
        var translationCount = await _unitOfWork.Repository<Translation>().GetQueryable().CountAsync(t => t.LanguageId == id, cancellationToken);
        return MapToDto(entity, translationCount);
    }

    public async Task<AdminLanguageDto> CreateAsync(AdminUpsertLanguageRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Language>();

        if (await repo.AnyAsync(x => x.Code == request.Code, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Code), "Bu dil kodu zaten kayitli.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        if (request.IsDefault)
        {
            await ClearExistingDefaultAsync(cancellationToken);
        }

        var entity = new Language
        {
            Code = request.Code.Trim().ToLowerInvariant(),
            Name = request.Name.Trim(),
            IsDefault = request.IsDefault,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await repo.AddAsync(entity, cancellationToken);

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LANGUAGE_CREATED", nameof(Language), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return MapToDto(entity, 0);
    }

    public async Task<AdminLanguageDto> UpdateAsync(long id, AdminUpsertLanguageRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Language>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Language), id);

        if (await repo.AnyAsync(x => x.Code == request.Code && x.Id != id, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Code), "Bu dil kodu zaten kayitli.");
        }

        if (entity.IsDefault && !request.IsDefault)
        {
            throw new ConflictException("Varsayilan dil kaldirilamaz. Once baska bir dili varsayilan yapin.");
        }

        var oldValue = new { entity.Code, entity.Name, entity.IsDefault, entity.IsActive };

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        if (request.IsDefault && !entity.IsDefault)
        {
            await ClearExistingDefaultAsync(cancellationToken);
        }

        entity.Code = request.Code.Trim().ToLowerInvariant();
        entity.Name = request.Name.Trim();
        entity.IsDefault = request.IsDefault;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LANGUAGE_UPDATED", nameof(Language), id, ipAddress, userAgent,
            oldValue, new { entity.Code, entity.Name, entity.IsDefault, entity.IsActive }, cancellationToken);

        var translationCount = await _unitOfWork.Repository<Translation>().GetQueryable().CountAsync(t => t.LanguageId == id, cancellationToken);
        return MapToDto(entity, translationCount);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Language>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Language), id);

        if (entity.IsDefault)
        {
            throw new ConflictException("Varsayilan dil silinemez. Once baska bir dili varsayilan yapin.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var translations = await _unitOfWork.Repository<Translation>().FindAsync(x => x.LanguageId == id, cancellationToken);
        _unitOfWork.Repository<Translation>().RemoveRange(translations);

        repo.Remove(entity);

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LANGUAGE_DELETED", nameof(Language), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    /// <summary>Yeni bir varsayilan dil atanmadan once mevcut varsayilan(lar)i temizler.</summary>
    private async Task ClearExistingDefaultAsync(CancellationToken cancellationToken)
    {
        var repo = _unitOfWork.Repository<Language>();
        var currentDefaults = await repo.FindAsync(x => x.IsDefault, cancellationToken);

        foreach (var lang in currentDefaults)
        {
            lang.IsDefault = false;
            lang.UpdatedAt = DateTime.UtcNow;
            repo.Update(lang);
        }
    }

    private async Task<Language> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Language>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Language), id);
    }

    private static AdminLanguageDto MapToDto(Language entity, int translationCount) => new()
    {
        Id = entity.Id,
        Code = entity.Code,
        Name = entity.Name,
        IsDefault = entity.IsDefault,
        IsActive = entity.IsActive,
        TranslationCount = translationCount,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };
}
