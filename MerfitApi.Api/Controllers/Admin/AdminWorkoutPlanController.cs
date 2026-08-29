using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.WorkoutPlans;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Kullanici antrenman planlarinin admin tarafindan incelenmesi (madde 13).</summary>
[Route("api/admin/workout-plans")]
public class AdminWorkoutPlanController : AdminControllerBase
{
    private readonly IAdminWorkoutPlanService _service;

    public AdminWorkoutPlanController(IAdminWorkoutPlanService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminWorkoutPlanListItemDto>>>> GetList(
        [FromQuery] AdminWorkoutPlanListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminWorkoutPlanDetailDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long id, [FromBody] AdminUpdateWorkoutPlanStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Planin gun/antrenman sirasini doner.</summary>
    [HttpGet("{id:long}/days")]
    public async Task<ActionResult<ApiResponse<List<AdminWorkoutPlanDayItemDto>>>> GetDays(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetDaysAsync(id, cancellationToken));

    /// <summary>Planin gun/antrenman sirasini tamamen degistirir (replace-all).</summary>
    [HttpPut("{id:long}/days")]
    public async Task<ActionResult<ApiResponse<List<AdminWorkoutPlanDayItemDto>>>> SetDays(
        long id, [FromBody] AdminSetWorkoutPlanDaysRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.SetDaysAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));
}
