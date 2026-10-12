using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>
/// Duyuru kampanyalari: taslak, kitle onizleme, zamanlama / hemen gonderme, iptal, istatistik,
/// alici sonuclari ve basarisiz gonderimlerin yeniden denenmesi. Gonderim istek icinde yapilmaz.
/// </summary>
[Route("api/admin/notification-campaigns")]
public class AdminNotificationCampaignController : AdminControllerBase
{
    private readonly IAdminNotificationCampaignService _service;
    private readonly INotificationAudienceResolver _audience;
    private readonly IAdminNotificationDispatchService _dispatches;

    public AdminNotificationCampaignController(
        IAdminNotificationCampaignService service, INotificationAudienceResolver audience, IAdminNotificationDispatchService dispatches)
    {
        _service = service;
        _audience = audience;
        _dispatches = dispatches;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminNotificationCampaignListItemDto>>>> GetList(
        [FromQuery] AdminNotificationCampaignListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminNotificationCampaignDetailDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    /// <summary>Kaydetmeden once kitle sayisi ve ornek alicilar.</summary>
    [HttpPost("audience-preview")]
    public async Task<ActionResult<ApiResponse<AdminNotificationAudiencePreviewDto>>> PreviewAudience(
        [FromBody] AdminNotificationAudienceRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _audience.PreviewAsync(request, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminNotificationCampaignDetailDto>>> Create(
        [FromBody] AdminUpsertNotificationCampaignRequest request, CancellationToken cancellationToken)
        => CreatedResponse(await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminNotificationCampaignDetailDto>>> Update(
        long id, [FromBody] AdminUpsertNotificationCampaignRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpGet("{id:long}/audience")]
    public async Task<ActionResult<ApiResponse<AdminNotificationAudiencePreviewDto>>> GetAudience(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.PreviewAudienceAsync(id, cancellationToken));

    /// <summary>Ileri bir zamana zamanlar (scheduledAtUtc bos ise hemen gonderir).</summary>
    [HttpPost("{id:long}/schedule")]
    public async Task<ActionResult<ApiResponse<AdminNotificationCampaignDetailDto>>> Schedule(
        long id, [FromBody] AdminScheduleNotificationCampaignRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.ScheduleAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Tek seferlik hemen gonderim; alicilar bir sonraki isleyici turunda kuyruga yazilir.</summary>
    [HttpPost("{id:long}/send-now")]
    public async Task<ActionResult<ApiResponse<AdminNotificationCampaignDetailDto>>> SendNow(
        long id, [FromBody] AdminScheduleNotificationCampaignRequest request, CancellationToken cancellationToken)
    {
        request.ScheduledAtUtc = null;
        return SuccessResponse(await _service.ScheduleAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));
    }

    [HttpPost("{id:long}/unschedule")]
    public async Task<ActionResult<ApiResponse<AdminNotificationCampaignDetailDto>>> Unschedule(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UnscheduleAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpPost("{id:long}/cancel")]
    public async Task<ActionResult<ApiResponse<AdminNotificationCampaignDetailDto>>> Cancel(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.CancelAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpGet("{id:long}/stats")]
    public async Task<ActionResult<ApiResponse<AdminNotificationCampaignStatsDto>>> GetStats(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetStatsAsync(id, cancellationToken));

    /// <summary>Alici bazli sonuclar (cihaz tokenlari donulmez).</summary>
    [HttpGet("{id:long}/recipients")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminNotificationDispatchDto>>>> GetRecipients(
        long id, [FromQuery] AdminNotificationDispatchListRequest request, CancellationToken cancellationToken)
    {
        await _service.GetByIdAsync(id, cancellationToken);
        request.CampaignId = id;
        request.Source = NotificationDispatchSource.Campaign;
        return SuccessResponse(await _dispatches.GetListAsync(request, cancellationToken));
    }

    /// <summary>Basarisiz alicilari (veya kuyruga yazilamamis kampanyayi) yeniden dener.</summary>
    [HttpPost("{id:long}/retry-failed")]
    public async Task<ActionResult<ApiResponse<AdminNotificationRetryResultDto>>> RetryFailed(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.RetryFailedAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken));
}
