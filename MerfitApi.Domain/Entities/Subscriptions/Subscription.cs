using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Subscriptions;

public sealed class Subscription : AuditableEntity
{
    public Guid UserId { get; set; }
    public SubscriptionProvider Provider { get; set; }
    public string? ExternalCustomerId { get; set; }
    public string? ExternalSubscriptionId { get; set; }
    public required string ProductId { get; set; }
    public required string Plan { get; set; }
    public SubscriptionStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool AutoRenew { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public SubscriptionEnvironment Environment { get; set; } = SubscriptionEnvironment.Production;
    public string? RawProviderData { get; set; }
}
