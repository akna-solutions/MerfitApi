using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.SubscriptionProducts;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Abonelik urunu (SubscriptionProduct) yonetimi servis sozlesmesi (madde 16/19).</summary>
public interface IAdminSubscriptionProductService
{
    Task<PagedResult<AdminSubscriptionProductDto>> GetListAsync(AdminSubscriptionProductListRequest request, CancellationToken cancellationToken = default);

    Task<AdminSubscriptionProductDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminSubscriptionProductDto> CreateAsync(AdminUpsertSubscriptionProductRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminSubscriptionProductDto> UpdateAsync(long id, AdminUpsertSubscriptionProductRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(long id, AdminUpdateSubscriptionProductStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<List<AdminSubscriptionProductFeatureItemDto>> GetFeaturesAsync(long id, CancellationToken cancellationToken = default);

    Task<List<AdminSubscriptionProductFeatureItemDto>> SetFeaturesAsync(long id, AdminSetSubscriptionProductFeaturesRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
