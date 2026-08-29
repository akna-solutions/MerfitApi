using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Features;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminFeatureService'in varsayilan implementasyonu.</summary>
public class AdminFeatureService : IAdminFeatureService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<Feature, object?>>> SortMap =
        new Dictionary<string, Expression<Func<Feature, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = x => x.Name,
            ["code"] = x => x.Code,
            ["createdAt"] = x => x.CreatedAt,
        };

    public AdminFeatureService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminFeatureDto>> GetListAsync(AdminFeatureListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Feature>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term));
        }

        var ordered = query.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "name");

        var mapped = ordered.Select(x => new AdminFeatureDto
        {
            Id = x.Id,
            Code = x.Code,
            Name = x.Name,
            Description = x.Description,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
        });

        return await mapped.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<AdminFeatureDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => MapToDto(await GetOrThrowAsync(id, cancellationToken));

    public async Task<AdminFeatureDto> CreateAsync(AdminUpsertFeatureRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Feature>();

        if (await repo.AnyAsync(x => x.Code == request.Code, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Code), "Bu kod zaten kullaniliyor.");
        }

        var entity = new Feature
        {
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Description = request.Description,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await repo.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FEATURE_CREATED", nameof(Feature), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return MapToDto(entity);
    }

    public async Task<AdminFeatureDto> UpdateAsync(long id, AdminUpsertFeatureRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Feature>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Feature), id);

        if (await repo.AnyAsync(x => x.Code == request.Code && x.Id != id, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Code), "Bu kod zaten kullaniliyor.");
        }

        var oldValue = new { entity.Code, entity.Name };

        entity.Code = request.Code.Trim().ToUpperInvariant();
        entity.Name = request.Name.Trim();
        entity.Description = request.Description;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FEATURE_UPDATED", nameof(Feature), id, ipAddress, userAgent, oldValue, new { entity.Code, entity.Name }, cancellationToken);

        return MapToDto(entity);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Feature>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Feature), id);

        var isUsedByProduct = await _unitOfWork.Repository<SubscriptionPlanFeature>().AnyAsync(x => x.FeatureId == id, cancellationToken);
        if (isUsedByProduct)
        {
            throw new ConflictException("Bu ozellik mevcut abonelik urunlerine bagli oldugu icin silinemez. Once urun-ozellik iliskisini kaldirin.");
        }

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FEATURE_DELETED", nameof(Feature), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    private async Task<Feature> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Feature>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Feature), id);
    }

    private static AdminFeatureDto MapToDto(Feature entity) => new()
    {
        Id = entity.Id,
        Code = entity.Code,
        Name = entity.Name,
        Description = entity.Description,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };
}
