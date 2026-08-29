using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Audit;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>
/// Admin tarafindan yapilan kritik islemlerin denetim (audit) kayitlarini listeleme/goruntuleme
/// uc noktalari (bkz. madde 32). Salt-okunurdur; audit kayitlari admin panelinden degistirilemez/silinemez.
/// </summary>
[Route("api/admin/audit-logs")]
public class AdminAuditLogController : AdminControllerBase
{
    private readonly IAuditLogService _auditLogService;

    public AdminAuditLogController(IAuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    /// <summary>Audit log kayitlarini admin kullanici, islem, varlik ve tarih araligina gore filtreleyerek sayfali listeler.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminAuditLogListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminAuditLogListItemDto>>>> GetList(
        [FromQuery] AdminAuditLogListRequest request, CancellationToken cancellationToken)
    {
        var result = await _auditLogService.GetListAsync(request, cancellationToken);
        return SuccessResponse(result);
    }

    /// <summary>Tek bir audit log kaydinin detayini (eski/yeni deger dahil) doner.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<AdminAuditLogDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminAuditLogDetailDto>>> GetById(long id, CancellationToken cancellationToken)
    {
        var result = await _auditLogService.GetByIdAsync(id, cancellationToken);
        return SuccessResponse(result);
    }
}
