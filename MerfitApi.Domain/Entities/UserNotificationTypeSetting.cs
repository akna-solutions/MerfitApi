using MerfitApi.Domain.Common;
using MerfitApi.Domain.Entities.Enums;

namespace MerfitApi.Domain.Entities;

/// <summary>
/// Kullanicinin bir bildirim turunu (NotificationType) acip kapattigi ayar. UserNotificationSetting'te
/// sutunu olmayan turler (orn. NutritionReminder) icin kullanilir; kayit yoksa turun varsayilani gecerlidir.
/// </summary>
public class UserNotificationTypeSetting : BaseEntity
{
    public long UserId { get; set; }

    public NotificationType Type { get; set; }

    public bool IsEnabled { get; set; }
}
