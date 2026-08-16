using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Notifications;

public sealed class ExpoPushToken : AuditableEntity
{
    public Guid UserId { get; set; }
    public required string Token { get; set; }
    public DevicePlatform Platform { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? AppVersion { get; set; }
    public string? Locale { get; set; }
    public string? Timezone { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
