using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Uygulama-ici bildirim (Notification) yonetimi uc noktalari (madde 20).</summary>
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

    /// <summary>Tek bir kullaniciya bildirim gonderir.</summary>
    [HttpPost("user")]
    public async Task<ActionResult<ApiResponse<AdminNotificationDetailDto>>> SendToUser(
        [FromBody] AdminSendUserNotificationRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.SendToUserAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    /// <summary>Tum aktif kullanicilara bildirim gonderir.</summary>
    [HttpPost("broadcast")]
    public async Task<ActionResult<ApiResponse<AdminNotificationSendResultDto>>> Broadcast(
        [FromBody] AdminBroadcastNotificationRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.BroadcastAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Belirli bir kullanici segmentine (AllUsers, PlusUsers, FreeUsers, InactiveUsers, NewUsers, WorkoutInactiveUsers) bildirim gonderir.</summary>
    [HttpPost("segment")]
    public async Task<ActionResult<ApiResponse<AdminNotificationSendResultDto>>> SendToSegment(
        [FromBody] AdminSegmentNotificationRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.SendToSegmentAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }
}
