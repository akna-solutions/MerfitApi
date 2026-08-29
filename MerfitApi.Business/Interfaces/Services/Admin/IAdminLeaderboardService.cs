using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Leaderboards;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Liderlik tablosu (LeaderboardPeriod/Entry) yonetimi servis sozlesmesi (madde 23).</summary>
public interface IAdminLeaderboardService
{
    Task<PagedResult<AdminLeaderboardPeriodDto>> GetPeriodsAsync(AdminLeaderboardPeriodListRequest request, CancellationToken cancellationToken = default);

    Task<AdminLeaderboardPeriodDto> GetPeriodByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminLeaderboardPeriodDto> CreatePeriodAsync(AdminUpsertLeaderboardPeriodRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminLeaderboardPeriodDto> UpdatePeriodAsync(long id, AdminUpsertLeaderboardPeriodRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeletePeriodAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminLeaderboardEntryDto>> GetEntriesAsync(long periodId, PagedRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Donem icin siralamayi yeniden hesaplar. Puanlama, donem tarih araliginda tamamlanan
    /// antrenman oturumu (WorkoutSession) sayisi uzerinden yapilir (bkz. AdminLeaderboardService yorumu).
    /// </summary>
    Task<AdminLeaderboardRecalculateResultDto> RecalculateAsync(long periodId, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
