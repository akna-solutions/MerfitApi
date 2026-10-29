using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Store;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>MB Shop kategori yonetimi.</summary>
public interface IAdminStoreCategoryService
{
    Task<PagedResult<AdminStoreCategoryDto>> GetListAsync(AdminStoreCategoryListRequest request, CancellationToken cancellationToken = default);
    Task<AdminStoreCategoryDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminStoreCategoryDto> CreateAsync(AdminUpsertStoreCategoryRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminStoreCategoryDto> UpdateAsync(long id, AdminUpsertStoreCategoryRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(long id, AdminUpdateStoreStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}

/// <summary>MB Shop urun yonetimi (fiyat, indirim, stok, gorsel, varyant).</summary>
public interface IAdminProductService
{
    Task<PagedResult<AdminProductListItemDto>> GetListAsync(AdminProductListRequest request, CancellationToken cancellationToken = default);
    Task<AdminProductDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminProductDto> CreateAsync(AdminUpsertProductRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminProductDto> UpdateAsync(long id, AdminUpsertProductRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(long id, AdminUpdateStoreStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminProductDto> AdjustStockAsync(long id, AdminAdjustStockRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}

/// <summary>MB Shop kampanya (urun grubu) yonetimi.</summary>
public interface IAdminStoreCampaignService
{
    Task<PagedResult<AdminStoreCampaignDto>> GetListAsync(AdminStoreCampaignListRequest request, CancellationToken cancellationToken = default);
    Task<AdminStoreCampaignDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminStoreCampaignDto> CreateAsync(AdminUpsertStoreCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminStoreCampaignDto> UpdateAsync(long id, AdminUpsertStoreCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(long id, AdminUpdateStoreStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}

/// <summary>MB Shop siparis yonetimi: listeleme, detay, durum ve odeme durumu gecisleri.</summary>
public interface IAdminStoreOrderService
{
    Task<PagedResult<AdminStoreOrderListItemDto>> GetListAsync(AdminStoreOrderListRequest request, CancellationToken cancellationToken = default);
    Task<AdminStoreOrderDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<AdminStoreOrderDto> UpdateStatusAsync(long id, AdminUpdateStoreOrderStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AdminStoreOrderDto> UpdatePaymentStatusAsync(long id, AdminUpdateStorePaymentStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
