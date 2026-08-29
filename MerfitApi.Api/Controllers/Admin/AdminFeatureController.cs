using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Features;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Ozellik (Feature) yonetimi uc noktalari (madde 19).</summary>
[Route("api/admin/features")]
public class AdminFeatureController : AdminControllerBase
{
    private readonly IAdminFeatureService _service;

    public AdminFeatureController(IAdminFeatureService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminFeatureDto>>>> GetList(
        [FromQuery] AdminFeatureListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminFeatureDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminFeatureDto>>> Create(
        [FromBody] AdminUpsertFeatureRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminFeatureDto>>> Update(
        long id, [FromBody] AdminUpsertFeatureRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }
}
