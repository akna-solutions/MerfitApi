using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Progress;

public sealed class BodyMetricEntry : BaseEntity
{
    public Guid UserId { get; set; }
    public MetricType MetricType { get; set; }
    public decimal Value { get; set; }
    public required string Unit { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
