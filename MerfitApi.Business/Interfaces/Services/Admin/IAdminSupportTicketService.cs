using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Support;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Destek talebi (SupportTicket) yonetimi servis sozlesmesi (madde 25).</summary>
public interface IAdminSupportTicketService
{
    Task<PagedResult<AdminSupportTicketListItemDto>> GetListAsync(AdminSupportTicketListRequest request, CancellationToken cancellationToken = default);

    Task<AdminSupportTicketDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(long id, AdminUpdateTicketStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task UpdatePriorityAsync(long id, AdminUpdateTicketPriorityRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminSupportTicketMessageDto>> GetMessagesAsync(long ticketId, PagedRequest request, CancellationToken cancellationToken = default);

    Task<AdminSupportTicketMessageDto> AddMessageAsync(long ticketId, AdminSendTicketMessageRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task CloseAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
