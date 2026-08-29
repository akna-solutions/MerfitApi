using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Dashboard;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Admin panelinin ana gosterge paneli (dashboard) uc noktalari.</summary>
[Route("api/admin/dashboard")]
public class AdminDashboardController : AdminControllerBase
{
    private readonly IAdminDashboardService _dashboardService;

    public AdminDashboardController(IAdminDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>Dashboard ana ekraninda gosterilen ozet metrikleri (toplam kullanici, gelir, vb.) doner.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<AdminDashboardSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminDashboardSummaryDto>>> GetSummary(CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetSummaryAsync(cancellationToken);
        return SuccessResponse(result);
    }

    /// <summary>Belirtilen tarih araliginda (varsayilan: son 30 gun) gunluk yeni kullanici sayisini doner.</summary>
    [HttpGet("user-growth")]
    [ProducesResponseType(typeof(ApiResponse<AdminUserGrowthDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminUserGrowthDto>>> GetUserGrowth(
        [FromQuery] AdminDashboardDateRangeRequest request, CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetUserGrowthAsync(request, cancellationToken);
        return SuccessResponse(result);
    }

    /// <summary>Belirtilen tarih araliginda (varsayilan: son 30 gun) gunluk/toplam geliri doner.</summary>
    [HttpGet("revenue")]
    [ProducesResponseType(typeof(ApiResponse<AdminRevenueDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminRevenueDto>>> GetRevenue(
        [FromQuery] AdminDashboardDateRangeRequest request, CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetRevenueAsync(request, cancellationToken);
        return SuccessResponse(result);
    }

    /// <summary>Abonelik durumlarina ve urunlere gore dagilimi doner.</summary>
    [HttpGet("subscriptions")]
    [ProducesResponseType(typeof(ApiResponse<AdminDashboardSubscriptionsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminDashboardSubscriptionsDto>>> GetSubscriptions(CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetSubscriptionsAsync(cancellationToken);
        return SuccessResponse(result);
    }

    /// <summary>Belirtilen tarih araliginda (varsayilan: son 30 gun) tamamlanan antrenman ve yeni kullanici aktivitesini doner.</summary>
    [HttpGet("activity")]
    [ProducesResponseType(typeof(ApiResponse<AdminDashboardActivityDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminDashboardActivityDto>>> GetActivity(
        [FromQuery] AdminDashboardDateRangeRequest request, CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetActivityAsync(request, cancellationToken);
        return SuccessResponse(result);
    }
}
