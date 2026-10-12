using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Bildirim sablonlari, mesaj onizlemesi ve form meta verileri.</summary>
[Route("api/admin/notification-templates")]
public class AdminNotificationTemplateController : AdminControllerBase
{
    private readonly IAdminNotificationTemplateService _service;

    public AdminNotificationTemplateController(IAdminNotificationTemplateService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminNotificationTemplateDto>>>> GetList(
        [FromQuery] AdminNotificationTemplateListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    /// <summary>Turler, segmentler, ekranlar, ture gore kullanilabilir alanlar ve buyuk kitle esigi.</summary>
    [HttpGet("metadata")]
    public ActionResult<ApiResponse<AdminNotificationMetadataDto>> GetMetadata() => SuccessResponse(_service.GetMetadata());

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminNotificationTemplateDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminNotificationTemplateDto>>> Create(
        [FromBody] AdminUpsertNotificationTemplateRequest request, CancellationToken cancellationToken)
        => CreatedResponse(await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminNotificationTemplateDto>>> Update(
        long id, [FromBody] AdminUpsertNotificationTemplateRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Metni ornek degerlerle (veya verilen kullanicinin adiyla) doldurur; hicbir sey gondermez.</summary>
    [HttpPost("preview")]
    public async Task<ActionResult<ApiResponse<AdminNotificationMessagePreviewDto>>> Preview(
        [FromBody] AdminNotificationMessagePreviewRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.PreviewAsync(request, cancellationToken));
}
