using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Content;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Uygulama icerigi (AppContent - banner/duyuru/kampanya vb.) yonetimi servis sozlesmesi (madde 27).</summary>
public interface IAdminContentService
{
    Task<PagedResult<AdminContentDto>> GetListAsync(AdminContentListRequest request, CancellationToken cancellationToken = default);

    Task<AdminContentDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminContentDto> CreateAsync(AdminUpsertContentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminContentDto> UpdateAsync(long id, AdminUpsertContentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(long id, AdminUpdateContentStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>Icerigi hemen yayina alir: IsActive=true yapar ve StartAt bos ise su ana ayarlar.</summary>
    Task<AdminContentDto> PublishAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
