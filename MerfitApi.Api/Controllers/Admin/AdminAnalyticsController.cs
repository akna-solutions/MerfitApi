using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Analytics;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Uygulama genelinde analitik metrikler uc noktalari (madde 33). Tamamen salt-okunurdur.</summary>
[Route("api/admin/analytics")]
public class AdminAnalyticsController : AdminControllerBase
{
    private readonly IAdminAnalyticsService _service;

    public AdminAnalyticsController(IAdminAnalyticsService service)
    {
        _service = service;
    }

    [HttpGet("users")]
    public async Task<ActionResult<ApiResponse<AdminAnalyticsUsersDto>>> GetUsers(CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetUsersAsync(cancellationToken));

    [HttpGet("workouts")]
    public async Task<ActionResult<ApiResponse<AdminAnalyticsWorkoutsDto>>> GetWorkouts(CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetWorkoutsAsync(cancellationToken));

    [HttpGet("nutrition")]
    public async Task<ActionResult<ApiResponse<AdminAnalyticsNutritionDto>>> GetNutrition(CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetNutritionAsync(cancellationToken));

    [HttpGet("subscriptions")]
    public async Task<ActionResult<ApiResponse<AdminAnalyticsSubscriptionsDto>>> GetSubscriptions(CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetSubscriptionsAsync(cancellationToken));

    [HttpGet("revenue")]
    public async Task<ActionResult<ApiResponse<AdminAnalyticsRevenueDto>>> GetRevenue(CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetRevenueAsync(cancellationToken));

    [HttpGet("retention")]
    public async Task<ActionResult<ApiResponse<AdminAnalyticsRetentionDto>>> GetRetention(CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetRetentionAsync(cancellationToken));

    [HttpGet("engagement")]
    public async Task<ActionResult<ApiResponse<AdminAnalyticsEngagementDto>>> GetEngagement(CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetEngagementAsync(cancellationToken));
}
