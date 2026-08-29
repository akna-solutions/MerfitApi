using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.SubscriptionTransactions;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Abonelik odeme islemlerinin (SubscriptionTransaction) salt-okunur incelenmesi (madde 18).</summary>
[Route("api/admin/subscription-transactions")]
public class AdminSubscriptionTransactionController : AdminControllerBase
{
    private readonly IAdminSubscriptionTransactionService _service;

    public AdminSubscriptionTransactionController(IAdminSubscriptionTransactionService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminSubscriptionTransactionListItemDto>>>> GetList(
        [FromQuery] AdminSubscriptionTransactionListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminSubscriptionTransactionDetailDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));
}
