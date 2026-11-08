using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.WorkoutLab;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>
/// Antrenman Laboratuvari. Testler kalici olarak saklanir ama gercek kullanicilardan tamamen bagimsizdir:
/// kullanici, plan, bildirim veya kisisellestirme isi olusturulmaz. Program MerfitCustomerApi'de gercek
/// WorkoutPlanGenerator ile uretilir; olusturma yaniti Pending doner, panel detay ucunu yoklar.
/// </summary>
[Route("api/admin/workout-lab")]
public class AdminWorkoutLabController : AdminControllerBase
{
    private readonly IAdminWorkoutLabService _service;

    public AdminWorkoutLabController(IAdminWorkoutLabService service)
    {
        _service = service;
    }

    /// <summary>Form secenekleri (hedefler, ekipman katalogu, sureler) ve katalog/kuyruk durumu.</summary>
    [HttpGet("options")]
    public async Task<ActionResult<ApiResponse<AdminWorkoutLabOptionsDto>>> GetOptions(CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetOptionsAsync(cancellationToken));

    [HttpGet("simulations")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminWorkoutLabSimulationListItemDto>>>> GetList(
        [FromQuery] AdminWorkoutLabSimulationListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("simulations/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminWorkoutLabSimulationDetailDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    /// <summary>Yeni test: profil kaydedilir ve program uretimi kuyruga alinir (201, Status = Pending).</summary>
    [HttpPost("simulations")]
    public async Task<ActionResult<ApiResponse<AdminWorkoutLabSimulationDetailDto>>> Create(
        [FromBody] AdminCreateWorkoutLabSimulationRequest request, CancellationToken cancellationToken)
        => CreatedResponse(await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Ayni profili guncel algoritma ve katalogla yeniden calistirir (yeni test kaydi).</summary>
    [HttpPost("simulations/{id:long}/rerun")]
    public async Task<ActionResult<ApiResponse<AdminWorkoutLabSimulationDetailDto>>> Rerun(long id, CancellationToken cancellationToken)
        => CreatedResponse(await _service.RerunAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpPut("simulations/{id:long}/evaluation")]
    public async Task<ActionResult<ApiResponse<AdminWorkoutLabSimulationDetailDto>>> Evaluate(
        long id, [FromBody] AdminEvaluateWorkoutLabSimulationRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.EvaluateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("simulations/{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpGet("compare")]
    public async Task<ActionResult<ApiResponse<AdminWorkoutLabComparisonDto>>> Compare(
        [FromQuery] long leftId, [FromQuery] long rightId, CancellationToken cancellationToken)
        => SuccessResponse(await _service.CompareAsync(leftId, rightId, cancellationToken));
}
