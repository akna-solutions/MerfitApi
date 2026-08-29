using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Support;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminSupportTicketService'in varsayilan implementasyonu.</summary>
public class AdminSupportTicketService : IAdminSupportTicketService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminSupportTicketService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminSupportTicketListItemDto>> GetListAsync(AdminSupportTicketListRequest request, CancellationToken cancellationToken = default)
    {
        var tickets = _unitOfWork.Repository<SupportTicket>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();
        var messages = _unitOfWork.Repository<SupportTicketMessage>().GetQueryable();

        if (request.Status.HasValue) tickets = tickets.Where(x => x.Status == request.Status.Value);
        if (request.Priority.HasValue) tickets = tickets.Where(x => x.Priority == request.Priority.Value);
        if (request.UserId.HasValue) tickets = tickets.Where(x => x.UserId == request.UserId.Value);
        if (request.CreatedFrom.HasValue) tickets = tickets.Where(x => x.CreatedAt >= request.CreatedFrom.Value);
        if (request.CreatedTo.HasValue) tickets = tickets.Where(x => x.CreatedAt <= request.CreatedTo.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            tickets = tickets.Where(x => x.Subject.Contains(term));
        }

        var totalCount = await tickets.LongCountAsync(cancellationToken);

        var raw = await tickets
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.Subject,
                x.Status,
                x.Priority,
                x.CreatedAt,
                x.ClosedAt,
                UserEmail = users.Where(u => u.Id == x.UserId).Select(u => u.Email).FirstOrDefault(),
                MessageCount = messages.Count(m => m.TicketId == x.Id),
            })
            .ToListAsync(cancellationToken);

        var items = raw.Select(x => new AdminSupportTicketListItemDto
        {
            Id = x.Id,
            UserId = x.UserId,
            UserEmail = x.UserEmail ?? "-",
            Subject = x.Subject,
            Status = x.Status.ToString(),
            Priority = x.Priority.ToString(),
            MessageCount = x.MessageCount,
            CreatedAt = x.CreatedAt,
            ClosedAt = x.ClosedAt,
        }).ToList();

        return PagedResult<AdminSupportTicketListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminSupportTicketDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);

        var userEmail = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == entity.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        var messageCount = await _unitOfWork.Repository<SupportTicketMessage>().GetQueryable()
            .CountAsync(m => m.TicketId == id, cancellationToken);

        return new AdminSupportTicketDetailDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            UserEmail = userEmail ?? "-",
            Subject = entity.Subject,
            Status = entity.Status.ToString(),
            Priority = entity.Priority.ToString(),
            MessageCount = messageCount,
            CreatedAt = entity.CreatedAt,
            ClosedAt = entity.ClosedAt,
            Message = entity.Message,
        };
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateTicketStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<SupportTicket>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(SupportTicket), id);

        var oldValue = new { entity.Status };
        entity.Status = request.Status;

        if (request.Status is SupportTicketStatus.Closed or SupportTicketStatus.Resolved)
        {
            entity.ClosedAt ??= DateTime.UtcNow;
        }
        else
        {
            entity.ClosedAt = null;
        }

        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "SUPPORT_TICKET_STATUS_CHANGED", nameof(SupportTicket), id, ipAddress, userAgent, oldValue, new { entity.Status }, cancellationToken);
    }

    public async Task UpdatePriorityAsync(long id, AdminUpdateTicketPriorityRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<SupportTicket>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(SupportTicket), id);

        var oldValue = new { entity.Priority };
        entity.Priority = request.Priority;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "SUPPORT_TICKET_PRIORITY_CHANGED", nameof(SupportTicket), id, ipAddress, userAgent, oldValue, new { entity.Priority }, cancellationToken);
    }

    public async Task<PagedResult<AdminSupportTicketMessageDto>> GetMessagesAsync(long ticketId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        var ticket = await GetOrThrowAsync(ticketId, cancellationToken);

        var messages = _unitOfWork.Repository<SupportTicketMessage>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        var query = from m in messages
                     where m.TicketId == ticketId
                     join u in users on m.SenderUserId equals u.Id into uj
                     from u in uj.DefaultIfEmpty()
                     orderby m.CreatedAt
                     select new AdminSupportTicketMessageDto
                     {
                         Id = m.Id,
                         TicketId = m.TicketId,
                         SenderUserId = m.SenderUserId,
                         SenderEmail = u != null ? u.Email : "-",
                         IsFromAdmin = m.SenderUserId != ticket.UserId,
                         Message = m.Message,
                         CreatedAt = m.CreatedAt,
                     };

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<AdminSupportTicketMessageDto> AddMessageAsync(long ticketId, AdminSendTicketMessageRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        if (adminUserId is null)
        {
            throw new Exception("Gecersiz oturum.");
        }

        var ticket = await GetOrThrowAsync(ticketId, cancellationToken);

        var message = new SupportTicketMessage
        {
            TicketId = ticketId,
            SenderUserId = adminUserId.Value,
            Message = request.Message.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.Repository<SupportTicketMessage>().AddAsync(message, cancellationToken);

        // Admin cevap yazdiginda talep "InProgress" durumuna gecirilir (halihazirda Closed/Resolved degilse).
        if (ticket.Status is SupportTicketStatus.Open)
        {
            var ticketRepo = _unitOfWork.Repository<SupportTicket>();
            var trackedTicket = await ticketRepo.GetQueryable(asNoTracking: false).FirstAsync(x => x.Id == ticketId, cancellationToken);
            trackedTicket.Status = SupportTicketStatus.InProgress;
            trackedTicket.UpdatedAt = DateTime.UtcNow;
            trackedTicket.UpdatedUser = adminUserId;
            ticketRepo.Update(trackedTicket);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "SUPPORT_TICKET_MESSAGE_SENT", nameof(SupportTicketMessage), message.Id, ipAddress, userAgent,
            newValue: new { ticketId, request.Message }, cancellationToken: cancellationToken);

        var adminEmail = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == adminUserId.Value).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        return new AdminSupportTicketMessageDto
        {
            Id = message.Id,
            TicketId = ticketId,
            SenderUserId = adminUserId.Value,
            SenderEmail = adminEmail ?? "-",
            IsFromAdmin = true,
            Message = message.Message,
            CreatedAt = message.CreatedAt,
        };
    }

    public async Task CloseAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<SupportTicket>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(SupportTicket), id);

        if (entity.Status == SupportTicketStatus.Closed)
        {
            throw new ConflictException("Bu talep zaten kapatilmis.");
        }

        entity.Status = SupportTicketStatus.Closed;
        entity.ClosedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "SUPPORT_TICKET_CLOSED", nameof(SupportTicket), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    private async Task<SupportTicket> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<SupportTicket>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(SupportTicket), id);
    }
}
