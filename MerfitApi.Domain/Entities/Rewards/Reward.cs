using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Rewards;

public sealed class Reward : BaseEntity
{
    public int Rank { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public string? ImageUrl { get; set; }
    public RewardType RewardType { get; set; }
    public required string Value { get; set; }
    public bool IsActive { get; set; } = true;
    public LeaderboardPeriod Period { get; set; } = LeaderboardPeriod.Weekly;
}

/// <summary>Placeholder for future redemption tracking; not required for MVP but modeled now so Reward doesn't need a breaking schema change later.</summary>
public sealed class RewardRedemption : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid RewardId { get; set; }
    public DateTimeOffset RedeemedAt { get; set; } = DateTimeOffset.UtcNow;
    public LeaderboardPeriod Period { get; set; }
    public int PeriodRank { get; set; }
}
