using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Users;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>
/// Admin panelinden kullanici yonetimi uc noktalari (madde 6). Kullanicinin kendisiyle ilgili
/// mutasyonlar (durum degistirme, soft-delete/restore) audit log'a yazilir; alt kaynaklar
/// (workout, meal, score, device vb.) salt-okunurdur.
/// </summary>
[Route("api/admin/users")]
public class AdminUserController : AdminControllerBase
{
    private readonly IAdminUserService _userService;

    public AdminUserController(IAdminUserService userService)
    {
        _userService = userService;
    }

    /// <summary>Kullanicilari arama/filtre/siralama destegiyle sayfali listeler.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserListItemDto>>>> GetList(
        [FromQuery] AdminUserListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetListAsync(request, cancellationToken));

    /// <summary>Bir kullanicinin aggregate ozet detayini (profil, abonelik, skor, streak vb.) doner.</summary>
    [HttpGet("{userId:long}")]
    public async Task<ActionResult<ApiResponse<AdminUserDetailDto>>> GetById(long userId, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetByIdAsync(userId, cancellationToken));

    /// <summary>Kullanicinin aktif/pasif durumunu degistirir (audit log'a yazilir).</summary>
    [HttpPatch("{userId:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long userId, [FromBody] AdminUpdateUserStatusRequest request, CancellationToken cancellationToken)
    {
        await _userService.UpdateStatusAsync(userId, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Kullaniciyi soft-delete eder (DeletedAt doldurulur, fiziksel silme yapilmaz).</summary>
    [HttpDelete("{userId:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long userId, CancellationToken cancellationToken)
    {
        await _userService.SoftDeleteAsync(userId, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Daha once soft-delete edilmis bir kullaniciyi geri yukler.</summary>
    [HttpPatch("{userId:long}/restore")]
    public async Task<ActionResult<ApiResponse>> Restore(long userId, CancellationToken cancellationToken)
    {
        await _userService.RestoreAsync(userId, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Kullanicinin fitness profilini (UserProfile) doner.</summary>
    [HttpGet("{userId:long}/profile")]
    public async Task<ActionResult<ApiResponse<AdminUserProfileDto>>> GetProfile(long userId, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetProfileAsync(userId, cancellationToken));

    /// <summary>Kullanicinin abonelik gecmisini sayfali listeler.</summary>
    [HttpGet("{userId:long}/subscriptions")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserSubscriptionListItemDto>>>> GetSubscriptions(
        long userId, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetSubscriptionsAsync(userId, request, cancellationToken));

    /// <summary>Kullanicinin tamamladigi/baslattigi antrenman oturumlarini (WorkoutSession) sayfali listeler.</summary>
    [HttpGet("{userId:long}/workouts")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserWorkoutSessionListItemDto>>>> GetWorkouts(
        long userId, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetWorkoutSessionsAsync(userId, request, cancellationToken));

    /// <summary>Kullanicinin antrenman planlarini sayfali listeler.</summary>
    [HttpGet("{userId:long}/workout-plans")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserWorkoutPlanListItemDto>>>> GetWorkoutPlans(
        long userId, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetWorkoutPlansAsync(userId, request, cancellationToken));

    /// <summary>Kullanicinin ogun (meal) kayitlarini sayfali listeler.</summary>
    [HttpGet("{userId:long}/meals")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserMealListItemDto>>>> GetMeals(
        long userId, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetMealsAsync(userId, request, cancellationToken));

    /// <summary>Kullanicinin guncel beslenme hedefini doner.</summary>
    [HttpGet("{userId:long}/nutrition-goals")]
    public async Task<ActionResult<ApiResponse<AdminUserNutritionGoalDto?>>> GetNutritionGoals(long userId, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetNutritionGoalAsync(userId, cancellationToken));

    /// <summary>Kullanicinin su tuketim kayitlarini sayfali listeler.</summary>
    [HttpGet("{userId:long}/water-logs")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserWaterLogListItemDto>>>> GetWaterLogs(
        long userId, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetWaterLogsAsync(userId, request, cancellationToken));

    /// <summary>Kullanicinin vucut olcum (BodyMeasurement) kayitlarini sayfali listeler.</summary>
    [HttpGet("{userId:long}/measurements")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserMeasurementListItemDto>>>> GetMeasurements(
        long userId, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetMeasurementsAsync(userId, request, cancellationToken));

    /// <summary>Kullanicinin guncel Merfit Score kaydini doner.</summary>
    [HttpGet("{userId:long}/score")]
    public async Task<ActionResult<ApiResponse<AdminUserScoreDto>>> GetScore(long userId, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetScoreAsync(userId, cancellationToken));

    /// <summary>Kullanicinin Merfit Score gecmisini sayfali listeler.</summary>
    [HttpGet("{userId:long}/score-history")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserScoreHistoryListItemDto>>>> GetScoreHistory(
        long userId, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetScoreHistoryAsync(userId, request, cancellationToken));

    /// <summary>Kullanicinin guncel Merfit Score'unun kategori kirilimini doner.</summary>
    [HttpGet("{userId:long}/score-breakdown")]
    public async Task<ActionResult<ApiResponse<List<AdminUserScoreBreakdownItemDto>>>> GetScoreBreakdown(long userId, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetScoreBreakdownAsync(userId, cancellationToken));

    /// <summary>Kullanicinin kazandigi basarimlari (achievements) sayfali listeler.</summary>
    [HttpGet("{userId:long}/achievements")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserAchievementListItemDto>>>> GetAchievements(
        long userId, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetAchievementsAsync(userId, request, cancellationToken));

    /// <summary>Kullanicinin kayitli cihazlarini (push bildirim hedefleri) sayfali listeler.</summary>
    [HttpGet("{userId:long}/devices")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserDeviceListItemDto>>>> GetDevices(
        long userId, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetDevicesAsync(userId, request, cancellationToken));

    /// <summary>Kullanicinin yasal metin onaylarini (consent) sayfali listeler. Bu veriler admin tarafindan degistirilemez.</summary>
    [HttpGet("{userId:long}/consents")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserConsentListItemDto>>>> GetConsents(
        long userId, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _userService.GetConsentsAsync(userId, request, cancellationToken));
}
