using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Content;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Uygulama icerigi (Banner/Announcement/Campaign/FeatureCard/Promotional) yonetimi uc noktalari (madde 27).</summary>
[Route("api/admin/content")]
public class AdminContentController : AdminControllerBase
{
    private readonly IAdminContentService _service;

    public AdminContentController(IAdminContentService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminContentDto>>>> GetList(
        [FromQuery] AdminContentListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminContentDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminContentDto>>> Create(
        [FromBody] AdminUpsertContentRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminContentDto>>> Update(
        long id, [FromBody] AdminUpsertContentRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long id, [FromBody] AdminUpdateContentStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Icerigi hemen yayina alir (IsActive=true, StartAt bos ise su ana ayarlanir).</summary>
    [HttpPost("{id:long}/publish")]
    public async Task<ActionResult<ApiResponse<AdminContentDto>>> Publish(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.PublishAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken));
}
