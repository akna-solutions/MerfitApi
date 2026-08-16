using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Profile;

public sealed class WeightEntry : BaseEntity
{
    public Guid UserId { get; set; }
    public decimal WeightKg { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
    public WeightEntrySource Source { get; set; } = WeightEntrySource.Manual;
    public string? Note { get; set; }
}
