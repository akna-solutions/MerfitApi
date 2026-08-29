using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Subscriptions;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Kullanici aboneliklerinin admin tarafindan incelenmesi/durum yonetimi uc noktalari (madde 17).</summary>
[Route("api/admin/subscriptions")]
public class AdminSubscriptionController : AdminControllerBase
{
    private readonly IAdminSubscriptionService _service;

    public AdminSubscriptionController(IAdminSubscriptionService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminSubscriptionListItemDto>>>> GetList(
        [FromQuery] AdminSubscriptionListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminSubscriptionDetailDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long id, [FromBody] AdminUpdateSubscriptionStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }
}
