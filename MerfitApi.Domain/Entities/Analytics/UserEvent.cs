using Merfit.Domain.Common;

namespace Merfit.Domain.Entities.Analytics;

public sealed class UserEvent : BaseEntity
{
    public Guid? UserId { get; set; }
    public required string EventName { get; set; }
    public string? PropertiesJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
