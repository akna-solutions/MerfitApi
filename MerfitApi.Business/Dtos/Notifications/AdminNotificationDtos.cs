using MerfitApi.Business.Common.Pagination;

namespace MerfitApi.Business.Dtos.Admin.Notifications;

public class AdminNotificationListItemDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    /// <summary>"MbFit" | "MbShop" (turden turetilir).</summary>
    public string App { get; set; } = "MbFit";
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
    /// <summary>Yalnizca MB Fit veya MB Shop bildirimleri.</summary>
    public MerfitApi.Domain.Entities.Enums.NotificationApp? App { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
