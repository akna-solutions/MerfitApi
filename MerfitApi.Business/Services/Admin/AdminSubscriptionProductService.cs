using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.SubscriptionProducts;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminSubscriptionProductService'in varsayilan implementasyonu.</summary>
public class AdminSubscriptionProductService : IAdminSubscriptionProductService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<SubscriptionProduct, object?>>> SortMap =
        new Dictionary<string, Expression<Func<SubscriptionProduct, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = x => x.Name,
            ["price"] = x => x.Price,
            ["createdAt"] = x => x.CreatedAt,
        };

    public AdminSubscriptionProductService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminSubscriptionProductDto>> GetListAsync(AdminSubscriptionProductListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<SubscriptionProduct>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term));
        }

        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);
        if (request.BillingPeriod.HasValue) query = query.Where(x => x.BillingPeriod == request.BillingPeriod.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var ordered = query.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "createdAt");

        var raw = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = raw.Select(MapToDto).ToList();

        return PagedResult<AdminSubscriptionProductDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminSubscriptionProductDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => MapToDto(await GetOrThrowAsync(id, cancellationToken));

    public async Task<AdminSubscriptionProductDto> CreateAsync(AdminUpsertSubscriptionProductRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<SubscriptionProduct>();

        if (await repo.AnyAsync(x => x.Code == request.Code, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Code), "Bu kod zaten kullaniliyor.");
        }

        var entity = new SubscriptionProduct
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            StoreProductIdIos = request.StoreProductIdIos,
            StoreProductIdAndroid = request.StoreProductIdAndroid,
            BillingPeriod = request.BillingPeriod,
            Price = request.Price,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await repo.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "SUBSCRIPTION_PRODUCT_CREATED", nameof(SubscriptionProduct), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return MapToDto(entity);
    }

    public async Task<AdminSubscriptionProductDto> UpdateAsync(long id, AdminUpsertSubscriptionProductRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<SubscriptionProduct>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(SubscriptionProduct), id);

        if (await repo.AnyAsync(x => x.Code == request.Code && x.Id != id, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Code), "Bu kod zaten kullaniliyor.");
        }

        var oldValue = new { entity.Code, entity.Price, entity.Currency, entity.IsActive };

        entity.Code = request.Code.Trim();
        entity.Name = request.Name.Trim();
        entity.StoreProductIdIos = request.StoreProductIdIos;
        entity.StoreProductIdAndroid = request.StoreProductIdAndroid;
        entity.BillingPeriod = request.BillingPeriod;
        entity.Price = request.Price;
        entity.Currency = request.Currency.Trim().ToUpperInvariant();
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "SUBSCRIPTION_PRODUCT_UPDATED", nameof(SubscriptionProduct), id, ipAddress, userAgent,
            oldValue, new { entity.Code, entity.Price, entity.Currency, entity.IsActive }, cancellationToken);

        return MapToDto(entity);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<SubscriptionProduct>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(SubscriptionProduct), id);

        var hasSubscriptions = await _unitOfWork.Repository<Subscription>().AnyAsync(x => x.SubscriptionProductId == id, cancellationToken);
        if (hasSubscriptions)
        {
            throw new ConflictException("Bu urune bagli abonelik kayitlari oldugu icin silinemez. Bunun yerine pasif hale getirebilirsiniz (PATCH /status).");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var planFeatures = await _unitOfWork.Repository<SubscriptionPlanFeature>().FindAsync(x => x.SubscriptionProductId == id, cancellationToken);
        _unitOfWork.Repository<SubscriptionPlanFeature>().RemoveRange(planFeatures);

        repo.Remove(entity);

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "SUBSCRIPTION_PRODUCT_DELETED", nameof(SubscriptionProduct), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateSubscriptionProductStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<SubscriptionProduct>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(SubscriptionProduct), id);

        var oldValue = new { entity.IsActive };
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "SUBSCRIPTION_PRODUCT_STATUS_CHANGED", nameof(SubscriptionProduct), id, ipAddress, userAgent, oldValue, new { entity.IsActive }, cancellationToken);
    }

    public async Task<List<AdminSubscriptionProductFeatureItemDto>> GetFeaturesAsync(long id, CancellationToken cancellationToken = default)
    {
        await GetOrThrowAsync(id, cancellationToken);

        var links = _unitOfWork.Repository<SubscriptionPlanFeature>().GetQueryable();
        var features = _unitOfWork.Repository<Feature>().GetQueryable();

        return await (from l in links
                       where l.SubscriptionProductId == id
                       join f in features on l.FeatureId equals f.Id
                       orderby f.Name
                       select new AdminSubscriptionProductFeatureItemDto
                       {
                           FeatureId = f.Id,
                           FeatureCode = f.Code,
                           FeatureName = f.Name,
                       }).ToListAsync(cancellationToken);
    }

    public async Task<List<AdminSubscriptionProductFeatureItemDto>> SetFeaturesAsync(long id, AdminSetSubscriptionProductFeaturesRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await GetOrThrowAsync(id, cancellationToken);

        var featureIds = request.FeatureIds.Distinct().ToList();
        if (featureIds.Count > 0)
        {
            var existingCount = await _unitOfWork.Repository<Feature>().GetQueryable()
                .CountAsync(f => featureIds.Contains(f.Id), cancellationToken);

            if (existingCount != featureIds.Count)
            {
                throw new AppValidationException(nameof(request.FeatureIds), "Listede bulunmayan bir ozellik (feature) Id'si var.");
            }
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var repo = _unitOfWork.Repository<SubscriptionPlanFeature>();
        var existing = await repo.FindAsync(x => x.SubscriptionProductId == id, cancellationToken);
        repo.RemoveRange(existing);

        var newLinks = featureIds.Select(featureId => new SubscriptionPlanFeature
        {
            SubscriptionProductId = id,
            FeatureId = featureId,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        }).ToList();

        await repo.AddRangeAsync(newLinks, cancellationToken);

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "SUBSCRIPTION_PRODUCT_FEATURES_SET", nameof(SubscriptionProduct), id, ipAddress, userAgent,
            oldValue: existing.Select(x => x.FeatureId), newValue: featureIds, cancellationToken: cancellationToken);

        return await GetFeaturesAsync(id, cancellationToken);
    }

    private async Task<SubscriptionProduct> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<SubscriptionProduct>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(SubscriptionProduct), id);
    }

    private static AdminSubscriptionProductDto MapToDto(SubscriptionProduct entity) => new()
    {
        Id = entity.Id,
        Code = entity.Code,
        Name = entity.Name,
        StoreProductIdIos = entity.StoreProductIdIos,
        StoreProductIdAndroid = entity.StoreProductIdAndroid,
        BillingPeriod = entity.BillingPeriod.ToString(),
        Price = entity.Price,
        Currency = entity.Currency,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };
}
