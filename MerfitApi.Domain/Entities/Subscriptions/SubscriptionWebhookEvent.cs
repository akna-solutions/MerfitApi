using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Subscriptions;

/// <summary>ExternalEventId must be unique — enforced at the database level — so a redelivered RevenueCat webhook is a no-op, not a double-processed event.</summary>
public sealed class SubscriptionWebhookEvent : BaseEntity
{
    public required string ExternalEventId { get; set; }
    public required string EventType { get; set; }
    public Guid? UserId { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public WebhookProcessingStatus ProcessingStatus { get; set; } = WebhookProcessingStatus.Pending;
    public string? ProcessingError { get; set; }
}
