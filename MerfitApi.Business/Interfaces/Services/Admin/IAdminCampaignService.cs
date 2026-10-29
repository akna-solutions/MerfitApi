using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Content;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Mobil ana sayfa kampanyalari (AppContent, Type = "Campaign") yonetimi servis sozlesmesi.</summary>
public interface IAdminCampaignService
{
    Task<PagedResult<AdminCampaignDto>> GetListAsync(AdminCampaignListRequest request, CancellationToken cancellationToken = default);

    Task<AdminCampaignDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminCampaignDto> CreateAsync(AdminUpsertCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminCampaignDto> UpdateAsync(long id, AdminUpsertCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(long id, AdminUpdateContentStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>Verilen kampanyalarin SortOrder degerlerini tek seferde gunceller.</summary>
    Task ReorderAsync(AdminReorderCampaignsRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
