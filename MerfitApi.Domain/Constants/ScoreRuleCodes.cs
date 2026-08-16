namespace Merfit.Domain.Constants;

/// <summary>
/// Canonical codes for ScoreRule.Code / ScoreTransaction.Type. Kept as string constants (not an
/// enum) so new rules can be added via seed/admin data without a code deployment, while giving
/// call sites compile-time-checked references for the built-in set required by the spec.
/// </summary>
public static class ScoreRuleCodes
{
    public const string WorkoutCompleted = "workout_completed";
    public const string PersonalRecord = "personal_record";
    public const string SevenDayStreak = "seven_day_streak";
    public const string ThirtyDayStreak = "thirty_day_streak";
    public const string NutritionGoalCompleted = "nutrition_goal_completed";
    public const string WeeklyGoalCompleted = "weekly_goal_completed";
}
