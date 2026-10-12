using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Otomatik bildirim kurallari (beslenme / antrenman hatirlatici, gunluk motivasyon).</summary>
[Route("api/admin/notification-rules")]
public class AdminNotificationRuleController : AdminControllerBase
{
    private readonly IAdminNotificationRuleService _service;

    public AdminNotificationRuleController(IAdminNotificationRuleService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminNotificationRuleDto>>>> GetList(
        [FromQuery] AdminNotificationRuleListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminNotificationRuleDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    /// <summary>Yeni kural duraklatilmis (Paused) olarak olusur.</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminNotificationRuleDto>>> Create(
        [FromBody] AdminUpsertNotificationRuleRequest request, CancellationToken cancellationToken)
        => CreatedResponse(await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminNotificationRuleDto>>> Update(
        long id, [FromBody] AdminUpsertNotificationRuleRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Active / Paused / Inactive.</summary>
    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse<AdminNotificationRuleDto>>> ChangeStatus(
        long id, [FromBody] AdminChangeNotificationRuleStatusRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.ChangeStatusAsync(id, request.Status, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }
}
