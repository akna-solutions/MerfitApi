using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Aylik MB FIT Puan odul bildirimleri: donem bazli durum, tetikleme ve yeniden deneme.</summary>
[Route("api/admin/notification-monthly-runs")]
public class AdminMonthlyRewardNotificationController : AdminControllerBase
{
    private readonly IAdminMonthlyRewardNotificationService _service;

    public AdminMonthlyRewardNotificationController(IAdminMonthlyRewardNotificationService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminMonthlyRewardNotificationRunDto>>>> GetList(
        [FromQuery] AdminMonthlyRewardNotificationRunListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("periods/{periodId:long}")]
    public async Task<ActionResult<ApiResponse<AdminMonthlyRewardNotificationRunDto>>> GetByPeriod(long periodId, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByPeriodAsync(periodId, cancellationToken));

    /// <summary>
    /// Isi kuyruga alir veya basarisiz / tamamlanmis isi yeniden baslatir. Donem bitmeden calismaz;
    /// daha once bildirim almis kazananlara tekrar gonderilmez.
    /// </summary>
    [HttpPost("periods/{periodId:long}/trigger")]
    public async Task<ActionResult<ApiResponse<AdminMonthlyRewardNotificationRunDto>>> Trigger(long periodId, CancellationToken cancellationToken)
        => SuccessResponse(await _service.TriggerAsync(periodId, CurrentAdminId, ClientIp, UserAgent, cancellationToken));
}
