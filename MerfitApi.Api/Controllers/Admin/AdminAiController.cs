using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Ai;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>AI istegi/sonucu incelemesi ve istatistikleri uc noktalari (madde 31). Tamamen salt-okunurdur.</summary>
[Route("api/admin/ai")]
public class AdminAiController : AdminControllerBase
{
    private readonly IAdminAiService _service;

    public AdminAiController(IAdminAiService service)
    {
        _service = service;
    }

    [HttpGet("requests")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminAiRequestListItemDto>>>> GetRequests(
        [FromQuery] AdminAiRequestListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetRequestsAsync(request, cancellationToken));

    [HttpGet("requests/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminAiRequestDetailDto>>> GetRequestById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetRequestByIdAsync(id, cancellationToken));

    [HttpGet("results")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminAiResultListItemDto>>>> GetResults(
        [FromQuery] AdminAiResultListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetResultsAsync(request, cancellationToken));

    [HttpGet("results/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminAiResultDetailDto>>> GetResultById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetResultByIdAsync(id, cancellationToken));

    [HttpGet("statistics")]
    public async Task<ActionResult<ApiResponse<AdminAiStatisticsDto>>> GetStatistics(CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetStatisticsAsync(cancellationToken));
}
