using Merfit.Domain.Common;

namespace Merfit.Domain.Entities.Achievements;

public sealed class Achievement : BaseEntity
{
    public required string Code { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Icon { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class UserAchievement : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid AchievementId { get; set; }
    public DateTimeOffset EarnedAt { get; set; } = DateTimeOffset.UtcNow;
}
