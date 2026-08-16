using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Profile;

/// <summary>1:1 with User, keyed by UserId. Holds the fitness profile collected during onboarding.</summary>
public sealed class UserProfile
{
    public Guid UserId { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? StartingWeightKg { get; set; }
    public decimal? TargetWeightKg { get; set; }
    public Goal? Goal { get; set; }
    public ActivityLevel? ActivityLevel { get; set; }
    public TrainingExperience? TrainingExperience { get; set; }
    public int? TrainingDaysPerWeek { get; set; }
    public TrainingLocation? TrainingLocation { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<UserEquipment> Equipment { get; set; } = [];
}

public sealed class UserEquipment
{
    public Guid UserId { get; set; }
    public Equipment Equipment { get; set; }
}
