using MerfitApi.Business.Common.Pagination;

namespace MerfitApi.Business.Dtos.Admin.Notifications;

public class AdminNotificationListItemDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminNotificationDetailDto : AdminNotificationListItemDto
{
    public string? DataJson { get; set; }
}

public class AdminNotificationListRequest : PagedRequest
{
    public long? UserId { get; set; }
    public bool? IsRead { get; set; }
    public MerfitApi.Domain.Entities.Enums.NotificationType? Type { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
