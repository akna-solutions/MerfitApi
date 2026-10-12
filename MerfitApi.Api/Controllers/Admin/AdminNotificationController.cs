using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>
/// Uygulama ici bildirim kutusu gecmisi. Gonderim icin kampanya uc noktalari kullanilir
/// (bkz. AdminNotificationCampaignController).
/// </summary>
[Route("api/admin/notifications")]
public class AdminNotificationController : AdminControllerBase
{
    private readonly IAdminNotificationService _service;

    public AdminNotificationController(IAdminNotificationService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminNotificationListItemDto>>>> GetList(
        [FromQuery] AdminNotificationListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminNotificationDetailDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }
}
