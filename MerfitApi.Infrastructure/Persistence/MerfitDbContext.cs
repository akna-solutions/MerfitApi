using Merfit.Domain.Entities.Achievements;
using Merfit.Domain.Entities.Analytics;
using Merfit.Domain.Entities.Identity;
using Merfit.Domain.Entities.Notifications;
using Merfit.Domain.Entities.Nutrition;
using Merfit.Domain.Entities.Profile;
using Merfit.Domain.Entities.Progress;
using Merfit.Domain.Entities.Rewards;
using Merfit.Domain.Entities.Scoring;
using Merfit.Domain.Entities.Subscriptions;
using Merfit.Domain.Entities.Workouts;
using Merfit.Domain.Entities.WorkoutSessions;
using Microsoft.EntityFrameworkCore;

namespace Merfit.Infrastructure.Persistence;

public sealed class MerfitDbContext(DbContextOptions<MerfitDbContext> options) : DbContext(options)
{
    // Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();

    // Profile
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserEquipment> UserEquipment => Set<UserEquipment>();
    public DbSet<WeightEntry> WeightEntries => Set<WeightEntry>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    // Workouts
    public DbSet<WorkoutCategory> WorkoutCategories => Set<WorkoutCategory>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<Workout> Workouts => Set<Workout>();
    public DbSet<WorkoutEquipment> WorkoutEquipment => Set<WorkoutEquipment>();
    public DbSet<WorkoutExercise> WorkoutExercises => Set<WorkoutExercise>();

    // Workout sessions
    public DbSet<WorkoutSession> WorkoutSessions => Set<WorkoutSession>();
    public DbSet<WorkoutExerciseLog> WorkoutExerciseLogs => Set<WorkoutExerciseLog>();
    public DbSet<SetLog> SetLogs => Set<SetLog>();
    public DbSet<PersonalRecord> PersonalRecords => Set<PersonalRecord>();
    public DbSet<UserWorkoutStreak> UserWorkoutStreaks => Set<UserWorkoutStreak>();

    // Nutrition
    public DbSet<Food> Foods => Set<Food>();
    public DbSet<DailyNutrition> DailyNutritions => Set<DailyNutrition>();
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<MealItem> MealItems => Set<MealItem>();

    // Progress
    public DbSet<BodyMetricEntry> BodyMetricEntries => Set<BodyMetricEntry>();

    // Scoring
    public DbSet<ScoreRule> ScoreRules => Set<ScoreRule>();
    public DbSet<ScoreTransaction> ScoreTransactions => Set<ScoreTransaction>();
    public DbSet<UserScore> UserScores => Set<UserScore>();

    // Achievements
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<UserAchievement> UserAchievements => Set<UserAchievement>();

    // Rewards
    public DbSet<Reward> Rewards => Set<Reward>();
    public DbSet<RewardRedemption> RewardRedemptions => Set<RewardRedemption>();

    // Subscriptions
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionWebhookEvent> SubscriptionWebhookEvents => Set<SubscriptionWebhookEvent>();

    // Notifications
    public DbSet<ExpoPushToken> ExpoPushTokens => Set<ExpoPushToken>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    // Analytics / audit
    public DbSet<UserEvent> UserEvents => Set<UserEvent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MerfitDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditableTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateAuditableTimestamps()
    {
        foreach (var entry in ChangeTracker.Entries<Merfit.Domain.Common.AuditableEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}
