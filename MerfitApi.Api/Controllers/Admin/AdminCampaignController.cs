using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Content;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Mobil ana sayfa kampanyalari yonetimi (AppContent, Type = "Campaign").</summary>
[Route("api/admin/campaigns")]
public class AdminCampaignController : AdminControllerBase
{
    private readonly IAdminCampaignService _service;

    public AdminCampaignController(IAdminCampaignService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminCampaignDto>>>> GetList(
        [FromQuery] AdminCampaignListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminCampaignDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminCampaignDto>>> Create(
        [FromBody] AdminUpsertCampaignRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminCampaignDto>>> Update(
        long id, [FromBody] AdminUpsertCampaignRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Aktif/pasif (arsivleme) durumunu degistirir.</summary>
    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long id, [FromBody] AdminUpdateContentStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Kampanyalarin gosterim sirasini toplu gunceller.</summary>
    [HttpPut("order")]
    public async Task<ActionResult<ApiResponse>> Reorder([FromBody] AdminReorderCampaignsRequest request, CancellationToken cancellationToken)
    {
        await _service.ReorderAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }
}
