using Merfit.Domain.Common;

namespace Merfit.Domain.Entities.Scoring;

/// <summary>
/// Configuration-driven point values (see Merfit.Domain.Constants.ScoreRuleCodes for the built-in
/// codes). Never hard-code point values in application code — always resolve through this table
/// (cached) so the values are editable/auditable without a redeploy.
/// </summary>
public sealed class ScoreRule : BaseEntity
{
    public required string Code { get; set; }
    public required string Description { get; set; }
    public int Points { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ScoreTransaction : BaseEntity
{
    public Guid UserId { get; set; }
    public required string Type { get; set; }
    public int Points { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Denormalized running total, kept in sync from ScoreTransaction (the source of truth) — never mutated independently.</summary>
public sealed class UserScore
{
    public Guid UserId { get; set; }
    public int TotalPoints { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
