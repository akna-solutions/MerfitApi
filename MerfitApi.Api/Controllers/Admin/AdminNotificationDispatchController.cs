using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Tum kaynaklardaki (kural, kampanya, aylik odul) gonderim kayitlari ve basarisiz gonderim incelemesi.</summary>
[Route("api/admin/notification-dispatches")]
public class AdminNotificationDispatchController : AdminControllerBase
{
    private readonly IAdminNotificationDispatchService _service;

    public AdminNotificationDispatchController(IAdminNotificationDispatchService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminNotificationDispatchDto>>>> GetList(
        [FromQuery] AdminNotificationDispatchListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    /// <summary>Gonderim detayi ve cihaz denemeleri (platform, hata kodu; token yok).</summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminNotificationDispatchDetailDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPost("{id:long}/retry")]
    public async Task<ActionResult<ApiResponse<AdminNotificationRetryResultDto>>> Retry(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.RetryAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken));
}
