using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Leaderboards;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Liderlik tablosu donemi ve girdileri yonetimi uc noktalari (madde 23).</summary>
[Route("api/admin")]
public class AdminLeaderboardController : AdminControllerBase
{
    private readonly IAdminLeaderboardService _service;

    public AdminLeaderboardController(IAdminLeaderboardService service)
    {
        _service = service;
    }

    [HttpGet("leaderboard-periods")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminLeaderboardPeriodDto>>>> GetPeriods(
        [FromQuery] AdminLeaderboardPeriodListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetPeriodsAsync(request, cancellationToken));

    [HttpGet("leaderboard-periods/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminLeaderboardPeriodDto>>> GetPeriodById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetPeriodByIdAsync(id, cancellationToken));

    [HttpPost("leaderboard-periods")]
    public async Task<ActionResult<ApiResponse<AdminLeaderboardPeriodDto>>> CreatePeriod(
        [FromBody] AdminUpsertLeaderboardPeriodRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreatePeriodAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("leaderboard-periods/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminLeaderboardPeriodDto>>> UpdatePeriod(
        long id, [FromBody] AdminUpsertLeaderboardPeriodRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdatePeriodAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("leaderboard-periods/{id:long}")]
    public async Task<ActionResult<ApiResponse>> DeletePeriod(long id, CancellationToken cancellationToken)
    {
        await _service.DeletePeriodAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Bir donemin siralama girdilerini (rank sirali) sayfali listeler.</summary>
    [HttpGet("leaderboards/{periodId:long}/entries")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminLeaderboardEntryDto>>>> GetEntries(
        long periodId, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetEntriesAsync(periodId, request, cancellationToken));

    /// <summary>Bir donemin siralamasini yeniden hesaplar (bkz. AdminLeaderboardService puanlama varsayimi).</summary>
    [HttpPost("leaderboards/{periodId:long}/recalculate")]
    public async Task<ActionResult<ApiResponse<AdminLeaderboardRecalculateResultDto>>> Recalculate(long periodId, CancellationToken cancellationToken)
    {
        var result = await _service.RecalculateAsync(periodId, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse(result);
    }
}
