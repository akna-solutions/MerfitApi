namespace Merfit.Domain.Enums;

public enum Gender
{
    Male,
    Female
}

public enum UnitSystem
{
    Metric,
    Imperial
}

/// <summary>Matches the frontend onboarding `Goal` union exactly (snake_case values serialized via JsonStringEnumMemberName in the API layer).</summary>
public enum Goal
{
    LoseWeight,
    BuildMuscle,
    GetStronger,
    ImproveFitness,
    MaintainWeight,
    ImproveEndurance
}

public enum ActivityLevel
{
    Sedentary,
    Light,
    Moderate,
    Active,
    Athlete
}

public enum TrainingExperience
{
    Beginner,
    Intermediate,
    Advanced
}

public enum TrainingLocation
{
    Gym,
    Home,
    Outdoor
}

/// <summary>Canonical equipment vocabulary. The workout catalog's display-cased equipment values map onto this same set (see docs/frontend-api-mapping.md).</summary>
public enum Equipment
{
    Dumbbells,
    Barbell,
    Bands,
    Machines,
    PullUpBar,
    Kettlebell,
    None
}

public enum WeightEntrySource
{
    Manual,
    Workout,
    Import
}
