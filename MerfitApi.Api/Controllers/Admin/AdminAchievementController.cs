using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Achievements;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Basarim (Achievement) yonetimi uc noktalari (madde 21).</summary>
[Route("api/admin/achievements")]
public class AdminAchievementController : AdminControllerBase
{
    private readonly IAdminAchievementService _service;

    public AdminAchievementController(IAdminAchievementService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminAchievementDto>>>> GetList(
        [FromQuery] AdminAchievementListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminAchievementDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminAchievementDto>>> Create(
        [FromBody] AdminUpsertAchievementRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminAchievementDto>>> Update(
        long id, [FromBody] AdminUpsertAchievementRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long id, [FromBody] AdminUpdateAchievementStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Bu basarimi kazanan kullanicilari sayfali listeler.</summary>
    [HttpGet("{id:long}/users")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminAchievementUserListItemDto>>>> GetUsers(
        long id, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetUsersAsync(id, request, cancellationToken));
}
