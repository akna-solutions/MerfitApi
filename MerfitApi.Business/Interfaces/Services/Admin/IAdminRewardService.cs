using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Rewards;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Odul (Reward) yonetimi servis sozlesmesi (madde 24).</summary>
public interface IAdminRewardService
{
    Task<PagedResult<AdminRewardDto>> GetListAsync(AdminRewardListRequest request, CancellationToken cancellationToken = default);

    Task<AdminRewardDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminRewardDto> CreateAsync(AdminUpsertRewardRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminRewardDto> UpdateAsync(long id, AdminUpsertRewardRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(long id, AdminUpdateRewardStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminLeaderboardRewardDto>> GetLeaderboardRewardsAsync(AdminLeaderboardRewardListRequest request, CancellationToken cancellationToken = default);

    Task<AdminLeaderboardRewardDto> CreateLeaderboardRewardAsync(AdminUpsertLeaderboardRewardRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminLeaderboardRewardDto> UpdateLeaderboardRewardAsync(long id, AdminUpsertLeaderboardRewardRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteLeaderboardRewardAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
