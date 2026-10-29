using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin.Store;

/// <summary>
/// MB Shop kampanyalari: magaza ana sayfasindaki kampanya kartlari ve kampanyaya bagli urun listesi. Mobil uygulama
/// yalnizca aktif, tarih araliginda ve icinde satistaki urun bulunan kampanyalari gosterir. Indirim tutari urunun
/// kendi indirimli fiyatindan gelir; kampanya ayrica fiyat degistirmez.
/// </summary>
public class AdminStoreCampaignService : IAdminStoreCampaignService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;
    private readonly TimeProvider _timeProvider;

    public AdminStoreCampaignService(IUnitOfWork unitOfWork, IAuditLogService auditLogService, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<AdminStoreCampaignDto>> GetListAsync(AdminStoreCampaignListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<StoreCampaign>().GetQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(c => c.Title.ToLower().Contains(term));
        }

        if (request.IsActive.HasValue) query = query.Where(c => c.IsActive == request.IsActive.Value);

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.Include(c => c.Products)
            .OrderBy(c => c.SortOrder).ThenByDescending(c => c.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var now = UtcNow;
        return PagedResult<AdminStoreCampaignDto>.Create(rows.Select(c => ToDto(c, now)).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<AdminStoreCampaignDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => ToDto(await GetOrThrowAsync(id, tracking: false, cancellationToken), UtcNow);

    public async Task<AdminStoreCampaignDto> CreateAsync(AdminUpsertStoreCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = new StoreCampaign { CreatedAt = UtcNow, CreatedUser = adminUserId };
        await ApplyAsync(entity, request, cancellationToken);
        await _unitOfWork.Repository<StoreCampaign>().AddAsync(entity, cancellationToken);
        await SaveAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "STORE_CAMPAIGN_CREATED", nameof(StoreCampaign), entity.Id, ipAddress, userAgent,
            newValue: Snapshot(entity), cancellationToken: cancellationToken);
        return ToDto(entity, UtcNow);
    }

    public async Task<AdminStoreCampaignDto> UpdateAsync(long id, AdminUpsertStoreCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, tracking: true, cancellationToken);
        var oldValue = Snapshot(entity);
        await ApplyAsync(entity, request, cancellationToken);
        entity.UpdatedAt = UtcNow;
        entity.UpdatedUser = adminUserId;
        await SaveAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "STORE_CAMPAIGN_UPDATED", nameof(StoreCampaign), id, ipAddress, userAgent,
            oldValue, Snapshot(entity), cancellationToken);
        return ToDto(entity, UtcNow);
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateStoreStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, tracking: true, cancellationToken);
        var oldValue = new { entity.IsActive };
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = UtcNow;
        entity.UpdatedUser = adminUserId;
        await SaveAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "STORE_CAMPAIGN_STATUS_CHANGED", nameof(StoreCampaign), id, ipAddress, userAgent,
            oldValue, new { entity.IsActive }, cancellationToken);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, tracking: true, cancellationToken);
        var oldValue = Snapshot(entity);
        _unitOfWork.Repository<StoreCampaign>().Remove(entity);
        await SaveAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "STORE_CAMPAIGN_DELETED", nameof(StoreCampaign), id, ipAddress, userAgent,
            oldValue, cancellationToken: cancellationToken);
    }

    private async Task ApplyAsync(StoreCampaign entity, AdminUpsertStoreCampaignRequest request, CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length < 2) throw new AppValidationException(nameof(request.Title), "Baslik zorunludur.");

        var startAt = AdminStoreInput.ToUtc(request.StartAt);
        var endAt = AdminStoreInput.ToUtc(request.EndAt);
        if (startAt.HasValue && endAt.HasValue && endAt.Value <= startAt.Value)
        {
            throw new AppValidationException(nameof(request.EndAt), "Bitis tarihi baslangic tarihinden sonra olmalidir.");
        }

        var productIds = request.ProductIds.Distinct().ToList();
        if (productIds.Count > 200) throw new AppValidationException(nameof(request.ProductIds), "Bir kampanyaya en fazla 200 urun eklenebilir.");
        var found = await _unitOfWork.Repository<Product>().GetQueryable().CountAsync(p => productIds.Contains(p.Id), cancellationToken);
        if (found != productIds.Count)
        {
            throw new AppValidationException(nameof(request.ProductIds), "Secilen urunlerden biri bulunamadi.");
        }

        entity.Title = title;
        entity.Description = AdminStoreInput.NullIfBlank(request.Description);
        entity.ImageUrl = AdminStoreInput.HttpsUrl(request.ImageUrl, nameof(request.ImageUrl));
        entity.Tint = AdminStoreInput.NullIfBlank(request.Tint)?.ToUpperInvariant();
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
        entity.StartAt = startAt;
        entity.EndAt = endAt;

        // Urun listesi istekteki sirayla esitlenir (ayni urun tekrar eklenmez; benzersiz index de bunu garanti eder).
        var now = UtcNow;
        foreach (var link in entity.Products.Where(l => !productIds.Contains(l.ProductId)).ToList())
        {
            entity.Products.Remove(link);
            _unitOfWork.Repository<StoreCampaignProduct>().Remove(link);
        }

        for (var index = 0; index < productIds.Count; index++)
        {
            var link = entity.Products.FirstOrDefault(l => l.ProductId == productIds[index]);
            if (link is null)
            {
                entity.Products.Add(new StoreCampaignProduct { ProductId = productIds[index], SortOrder = index, CreatedAt = now });
            }
            else if (link.SortOrder != index)
            {
                link.SortOrder = index;
                link.UpdatedAt = now;
            }
        }
    }

    private Task SaveAsync(CancellationToken cancellationToken) =>
        AdminStoreInput.SaveAsync(_unitOfWork, _ => "Ayni urun kampanyaya birden fazla kez eklenemez.", cancellationToken);

    private async Task<StoreCampaign> GetOrThrowAsync(long id, bool tracking, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<StoreCampaign>().GetQueryable(asNoTracking: !tracking)
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
        ?? throw new NotFoundException("Kampanya", id);

    public static string ResolveStatus(StoreCampaign c, DateTime nowUtc)
    {
        if (!c.IsActive) return "Passive";
        if (c.StartAt.HasValue && c.StartAt.Value > nowUtc) return "Scheduled";
        if (c.EndAt.HasValue && c.EndAt.Value < nowUtc) return "Ended";
        return "Live";
    }

    private static AdminStoreCampaignDto ToDto(StoreCampaign c, DateTime nowUtc) => new()
    {
        Id = c.Id,
        Title = c.Title,
        Description = c.Description,
        ImageUrl = c.ImageUrl,
        Tint = c.Tint,
        SortOrder = c.SortOrder,
        IsActive = c.IsActive,
        StartAt = c.StartAt,
        EndAt = c.EndAt,
        Status = ResolveStatus(c, nowUtc),
        ProductIds = c.Products.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).Select(p => p.ProductId).ToList(),
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };

    private static object Snapshot(StoreCampaign c) => new
    {
        c.Title, c.IsActive, c.StartAt, c.EndAt, c.SortOrder, ProductIds = c.Products.Select(p => p.ProductId).ToList(),
    };
}
