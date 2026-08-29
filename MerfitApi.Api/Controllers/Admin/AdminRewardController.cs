using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Rewards;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Odul (Reward) yonetimi ve liderlik tablosu-odul iliskisi uc noktalari (madde 24).</summary>
[Route("api/admin")]
public class AdminRewardController : AdminControllerBase
{
    private readonly IAdminRewardService _service;

    public AdminRewardController(IAdminRewardService service)
    {
        _service = service;
    }

    [HttpGet("rewards")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminRewardDto>>>> GetList(
        [FromQuery] AdminRewardListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("rewards/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminRewardDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPost("rewards")]
    public async Task<ActionResult<ApiResponse<AdminRewardDto>>> Create(
        [FromBody] AdminUpsertRewardRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("rewards/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminRewardDto>>> Update(
        long id, [FromBody] AdminUpsertRewardRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("rewards/{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpPatch("rewards/{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long id, [FromBody] AdminUpdateRewardStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Liderlik tablosu donem-siralama-odul iliskilerini listeler (periodId ile filtrelenebilir).</summary>
    [HttpGet("leaderboard-rewards")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminLeaderboardRewardDto>>>> GetLeaderboardRewards(
        [FromQuery] AdminLeaderboardRewardListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetLeaderboardRewardsAsync(request, cancellationToken));

    [HttpPost("leaderboard-rewards")]
    public async Task<ActionResult<ApiResponse<AdminLeaderboardRewardDto>>> CreateLeaderboardReward(
        [FromBody] AdminUpsertLeaderboardRewardRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateLeaderboardRewardAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("leaderboard-rewards/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminLeaderboardRewardDto>>> UpdateLeaderboardReward(
        long id, [FromBody] AdminUpsertLeaderboardRewardRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateLeaderboardRewardAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("leaderboard-rewards/{id:long}")]
    public async Task<ActionResult<ApiResponse>> DeleteLeaderboardReward(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteLeaderboardRewardAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }
}
