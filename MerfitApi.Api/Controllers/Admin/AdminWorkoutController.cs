using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Workouts;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>
/// Antrenman (Workout) yonetimi uc noktalari (madde 11). Egzersiz/ekipman listeleri
/// aggregate (tam degistirme) mantigiyla tek PUT uzerinden yonetilir.
/// </summary>
[Route("api/admin/workouts")]
public class AdminWorkoutController : AdminControllerBase
{
    private readonly IAdminWorkoutService _service;

    public AdminWorkoutController(IAdminWorkoutService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminWorkoutListItemDto>>>> GetList(
        [FromQuery] AdminWorkoutListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminWorkoutDetailDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminWorkoutDetailDto>>> Create(
        [FromBody] AdminUpsertWorkoutRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminWorkoutDetailDto>>> Update(
        long id, [FromBody] AdminUpsertWorkoutRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long id, [FromBody] AdminUpdateWorkoutStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpPatch("{id:long}/featured")]
    public async Task<ActionResult<ApiResponse>> UpdateFeatured(long id, [FromBody] AdminUpdateWorkoutFeaturedRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateFeaturedAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpPatch("{id:long}/premium")]
    public async Task<ActionResult<ApiResponse>> UpdatePremium(long id, [FromBody] AdminUpdateWorkoutPremiumRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdatePremiumAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Antrenmanin guncel egzersiz listesini (sira/set/tekrar dahil) doner.</summary>
    [HttpGet("{id:long}/exercises")]
    public async Task<ActionResult<ApiResponse<List<AdminWorkoutExerciseItemDto>>>> GetExercises(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetExercisesAsync(id, cancellationToken));

    /// <summary>Antrenmanin egzersiz listesini tamamen degistirir (replace-all).</summary>
    [HttpPut("{id:long}/exercises")]
    public async Task<ActionResult<ApiResponse<List<AdminWorkoutExerciseItemDto>>>> SetExercises(
        long id, [FromBody] AdminSetWorkoutExercisesRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.SetExercisesAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Antrenmanin guncel ekipman listesini doner.</summary>
    [HttpGet("{id:long}/equipment")]
    public async Task<ActionResult<ApiResponse<List<AdminWorkoutEquipmentItemDto>>>> GetEquipment(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetEquipmentAsync(id, cancellationToken));

    /// <summary>Antrenmanin ekipman listesini tamamen degistirir (replace-all).</summary>
    [HttpPut("{id:long}/equipment")]
    public async Task<ActionResult<ApiResponse<List<AdminWorkoutEquipmentItemDto>>>> SetEquipment(
        long id, [FromBody] AdminSetWorkoutEquipmentRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.SetEquipmentAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));
}
