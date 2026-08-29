using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Users;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>
/// Admin panelinden kullanici yonetimi (listeleme, durum degistirme, soft-delete/restore) ve
/// bir kullanicinin ait oldugu verileri (profil, abonelik, antrenman, beslenme, skor vb.)
/// salt-okunur olarak inceleme (madde 6) is kurallarini yuruten servis sozlesmesi.
/// </summary>
public interface IAdminUserService
{
    Task<PagedResult<AdminUserListItemDto>> GetListAsync(AdminUserListRequest request, CancellationToken cancellationToken = default);

    Task<AdminUserDetailDto> GetByIdAsync(long userId, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(long userId, AdminUpdateUserStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(long userId, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task RestoreAsync(long userId, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminUserProfileDto> GetProfileAsync(long userId, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminUserSubscriptionListItemDto>> GetSubscriptionsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminUserWorkoutSessionListItemDto>> GetWorkoutSessionsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminUserWorkoutPlanListItemDto>> GetWorkoutPlansAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminUserMealListItemDto>> GetMealsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default);

    Task<AdminUserNutritionGoalDto?> GetNutritionGoalAsync(long userId, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminUserWaterLogListItemDto>> GetWaterLogsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminUserMeasurementListItemDto>> GetMeasurementsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default);

    Task<AdminUserScoreDto> GetScoreAsync(long userId, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminUserScoreHistoryListItemDto>> GetScoreHistoryAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default);

    Task<List<AdminUserScoreBreakdownItemDto>> GetScoreBreakdownAsync(long userId, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminUserAchievementListItemDto>> GetAchievementsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminUserDeviceListItemDto>> GetDevicesAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminUserConsentListItemDto>> GetConsentsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default);
}
