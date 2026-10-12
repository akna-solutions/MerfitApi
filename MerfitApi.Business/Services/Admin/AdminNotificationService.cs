using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>
/// Uygulama ici bildirim kutusu gecmisi (Notification kayitlari). Yeni bildirimler artik dogrudan
/// burada olusturulmaz: admin duyurulari kampanya (AdminNotificationCampaignService), otomatik
/// bildirimler kural/odul isleri uzerinden ortak NotificationDispatch kuyruguna yazilir ve
/// MerfitCustomerApi'deki gonderim motoru tercihleri/sessiz saatleri uygulayarak kutuya ve push'a iletir.
/// </summary>
public class AdminNotificationService : IAdminNotificationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminNotificationService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminNotificationListItemDto>> GetListAsync(AdminNotificationListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Notification>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        if (request.UserId.HasValue) query = query.Where(x => x.UserId == request.UserId.Value);
        if (request.IsRead.HasValue) query = query.Where(x => x.IsRead == request.IsRead.Value);
        if (request.Type.HasValue) query = query.Where(x => x.Type == request.Type.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(search) || users.Any(u => u.Id == x.UserId && u.Email.ToLower().Contains(search)));
        }
        if (request.From.HasValue) query = query.Where(x => x.CreatedAt >= request.From.Value);
        if (request.To.HasValue) query = query.Where(x => x.CreatedAt <= request.To.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);

        var raw = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.Type,
                x.Title,
                x.Body,
                x.ImageUrl,
                x.IsRead,
                x.ReadAt,
                x.ExpiresAt,
                x.CreatedAt,
                UserEmail = users.Where(u => u.Id == x.UserId).Select(u => u.Email).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = raw.Select(x => new AdminNotificationListItemDto
        {
            Id = x.Id,
            UserId = x.UserId,
            UserEmail = x.UserEmail ?? "-",
            Type = x.Type.ToString(),
            Title = x.Title,
            Body = x.Body,
            ImageUrl = x.ImageUrl,
            IsRead = x.IsRead,
            ReadAt = x.ReadAt,
            ExpiresAt = x.ExpiresAt,
            CreatedAt = x.CreatedAt,
        }).ToList();

        return PagedResult<AdminNotificationListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminNotificationDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);

        var userEmail = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == entity.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        return MapToDetail(entity, userEmail ?? "-");
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Notification>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Notification), id);

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_DELETED", nameof(Notification), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    private async Task<Notification> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Notification>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Notification), id);
    }

    private static AdminNotificationDetailDto MapToDetail(Notification entity, string userEmail) => new()
    {
        Id = entity.Id,
        UserId = entity.UserId,
        UserEmail = userEmail,
        Type = entity.Type.ToString(),
        Title = entity.Title,
        Body = entity.Body,
        ImageUrl = entity.ImageUrl,
        IsRead = entity.IsRead,
        ReadAt = entity.ReadAt,
        ExpiresAt = entity.ExpiresAt,
        CreatedAt = entity.CreatedAt,
        DataJson = entity.DataJson,
    };
}
