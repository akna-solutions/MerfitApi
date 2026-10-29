using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>MB Shop magaza kampanyalari (urun gruplari). Ana sayfa kampanyalari icin bkz. AdminCampaignController.</summary>
[Route("api/admin/store/campaigns")]
public class AdminStoreCampaignController : AdminControllerBase
{
    private readonly IAdminStoreCampaignService _service;

    public AdminStoreCampaignController(IAdminStoreCampaignService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminStoreCampaignDto>>>> GetList(
        [FromQuery] AdminStoreCampaignListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminStoreCampaignDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminStoreCampaignDto>>> Create(
        [FromBody] AdminUpsertStoreCampaignRequest request, CancellationToken cancellationToken)
        => CreatedResponse(await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminStoreCampaignDto>>> Update(
        long id, [FromBody] AdminUpsertStoreCampaignRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long id, [FromBody] AdminUpdateStoreStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }
}
