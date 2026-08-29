using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Features;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Ozellik (Feature) yonetimi servis sozlesmesi (madde 19).</summary>
public interface IAdminFeatureService
{
    Task<PagedResult<AdminFeatureDto>> GetListAsync(AdminFeatureListRequest request, CancellationToken cancellationToken = default);

    Task<AdminFeatureDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminFeatureDto> CreateAsync(AdminUpsertFeatureRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminFeatureDto> UpdateAsync(long id, AdminUpsertFeatureRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
