using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Support;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Destek talebi (SupportTicket) yonetimi uc noktalari (madde 25).</summary>
[Route("api/admin/support/tickets")]
public class AdminSupportTicketController : AdminControllerBase
{
    private readonly IAdminSupportTicketService _service;

    public AdminSupportTicketController(IAdminSupportTicketService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminSupportTicketListItemDto>>>> GetList(
        [FromQuery] AdminSupportTicketListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminSupportTicketDetailDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long id, [FromBody] AdminUpdateTicketStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpPatch("{id:long}/priority")]
    public async Task<ActionResult<ApiResponse>> UpdatePriority(long id, [FromBody] AdminUpdateTicketPriorityRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdatePriorityAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpGet("{id:long}/messages")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminSupportTicketMessageDto>>>> GetMessages(
        long id, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetMessagesAsync(id, request, cancellationToken));

    /// <summary>Talebe admin adina bir cevap mesaji ekler; sender bilgisi JWT'den alinan admin Id'sidir.</summary>
    [HttpPost("{id:long}/messages")]
    public async Task<ActionResult<ApiResponse<AdminSupportTicketMessageDto>>> AddMessage(
        long id, [FromBody] AdminSendTicketMessageRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.AddMessageAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPost("{id:long}/close")]
    public async Task<ActionResult<ApiResponse>> Close(long id, CancellationToken cancellationToken)
    {
        await _service.CloseAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }
}
