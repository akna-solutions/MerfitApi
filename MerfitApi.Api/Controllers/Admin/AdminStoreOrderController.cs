using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>MB Shop siparisleri: filtreli liste, detay, siparis ve odeme durumu gecisleri.</summary>
[Route("api/admin/store/orders")]
public class AdminStoreOrderController : AdminControllerBase
{
    private readonly IAdminStoreOrderService _service;

    public AdminStoreOrderController(IAdminStoreOrderService service)
    {
        _service = service;
    }

    /// <summary>Filtreler: status, paymentStatus, paymentMethod, userId, from, to, search (siparis no / e-posta).</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminStoreOrderListItemDto>>>> GetList(
        [FromQuery] AdminStoreOrderListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminStoreOrderDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    /// <summary>Siparis durumunu degistirir. Gecersiz gecis 409; iptal stogu iade eder.</summary>
    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse<AdminStoreOrderDto>>> UpdateStatus(
        long id, [FromBody] AdminUpdateStoreOrderStatusRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Odeme durumunu degistirir (orn. havale onayi). Gecersiz gecis 409.</summary>
    [HttpPatch("{id:long}/payment-status")]
    public async Task<ActionResult<ApiResponse<AdminStoreOrderDto>>> UpdatePaymentStatus(
        long id, [FromBody] AdminUpdateStorePaymentStatusRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdatePaymentStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));
}
