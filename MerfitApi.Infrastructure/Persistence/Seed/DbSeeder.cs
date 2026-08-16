using Merfit.Application.Common.Interfaces;
using Merfit.Domain.Constants;
using Merfit.Domain.Entities.Achievements;
using Merfit.Domain.Entities.Identity;
using Merfit.Domain.Entities.Notifications;
using Merfit.Domain.Entities.Profile;
using Merfit.Domain.Entities.Scoring;
using Merfit.Domain.Entities.WorkoutSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Merfit.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent development/demo seed. Every step checks for existing data before inserting, so
/// running the seeder repeatedly (e.g. on every dev `dotnet run`) is always safe.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<MerfitDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher>();
        var logger = services.GetRequiredService<ILogger<MerfitDbContext>>();

        await SeedRolesAsync(db);
        await SeedScoreRulesAsync(db);
        await SeedAchievementsAsync(db);
        await WorkoutSeedData.SeedAsync(db);
        await SeedUsersAsync(db, hasher);

        await db.SaveChangesAsync();
        logger.LogInformation("Database seed complete.");
    }

    private static async Task SeedRolesAsync(MerfitDbContext db)
    {
        foreach (var name in new[] { RoleNames.User, RoleNames.Admin, RoleNames.Moderator })
        {
            if (!await db.Roles.AnyAsync(r => r.Name == name))
            {
                await db.Roles.AddAsync(new Role { Name = name });
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedScoreRulesAsync(MerfitDbContext db)
    {
        var rules = new (string Code, string Description, int Points)[]
        {
            (ScoreRuleCodes.WorkoutCompleted, "Awarded when a workout session is completed.", 100),
            (ScoreRuleCodes.PersonalRecord, "Awarded when a new personal record is set.", 150),
            (ScoreRuleCodes.SevenDayStreak, "Awarded when a 7-day workout streak is reached.", 200),
            (ScoreRuleCodes.ThirtyDayStreak, "Awarded when a 30-day workout streak is reached.", 500),
            (ScoreRuleCodes.NutritionGoalCompleted, "Awarded when the daily nutrition target is met.", 50),
            (ScoreRuleCodes.WeeklyGoalCompleted, "Awarded when the weekly workout goal is met.", 200),
        };

        foreach (var (code, description, points) in rules)
        {
            if (!await db.ScoreRules.AnyAsync(r => r.Code == code))
            {
                await db.ScoreRules.AddAsync(new ScoreRule { Code = code, Description = description, Points = points, IsActive = true });
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedAchievementsAsync(MerfitDbContext db)
    {
        var achievements = new (string Code, string Title, string Description, string Icon, int Sort)[]
        {
            (AchievementCodes.FirstWorkout, "First Workout", "Complete your first workout.", "workouts", 1),
            (AchievementCodes.SevenDayStreak, "7 Day Streak", "Work out 7 days in a row.", "streak", 2),
            (AchievementCodes.ThirtyDayStreak, "30 Day Streak", "Work out 30 days in a row.", "streak", 3),
            (AchievementCodes.FirstPersonalRecord, "First PR", "Set your first personal record.", "pr", 4),
            (AchievementCodes.TenWorkouts, "10 Workouts", "Complete 10 workouts.", "workouts", 5),
            (AchievementCodes.TwentyFiveWorkouts, "25 Workouts", "Complete 25 workouts.", "workouts", 6),
            (AchievementCodes.FiftyWorkouts, "50 Workouts", "Complete 50 workouts.", "workouts", 7),
            (AchievementCodes.WeeklyGoal, "Weekly Goal", "Hit your weekly workout goal.", "weekly", 8),
            (AchievementCodes.NutritionGoal, "Nutrition Goal", "Hit your daily nutrition target.", "weekly", 9),
        };

        foreach (var (code, title, description, icon, sort) in achievements)
        {
            if (!await db.Achievements.AnyAsync(a => a.Code == code))
            {
                await db.Achievements.AddAsync(new Achievement { Code = code, Title = title, Description = description, Icon = icon, SortOrder = sort });
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedUsersAsync(MerfitDbContext db, IPasswordHasher hasher)
    {
        var adminRole = await db.Roles.FirstAsync(r => r.Name == RoleNames.Admin);
        var userRole = await db.Roles.FirstAsync(r => r.Name == RoleNames.User);

        if (!await db.Users.AnyAsync(u => u.NormalizedEmail == "ADMIN@MERFIT.APP"))
        {
            var admin = new User
            {
                Email = "admin@merfit.app",
                NormalizedEmail = "ADMIN@MERFIT.APP",
                Username = "admin",
                PasswordHash = hasher.Hash("Admin123!"),
                FirstName = "Merfit",
                LastName = "Admin",
                IsEmailVerified = true,
                IsActive = true,
            };
            await db.Users.AddAsync(admin);
            await db.SaveChangesAsync();

            await db.UserRoles.AddAsync(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });
            await SeedProvisioningRowsAsync(db, admin.Id);
        }

        if (!await db.Users.AnyAsync(u => u.NormalizedEmail == "DEMO@MERFIT.APP"))
        {
            var demo = new User
            {
                Email = "demo@merfit.app",
                NormalizedEmail = "DEMO@MERFIT.APP",
                Username = "demo",
                PasswordHash = hasher.Hash("Demo1234!"),
                FirstName = "Mert",
                LastName = "Demo",
                IsEmailVerified = true,
                IsActive = true,
            };
            await db.Users.AddAsync(demo);
            await db.SaveChangesAsync();

            await db.UserRoles.AddAsync(new UserRole { UserId = demo.Id, RoleId = userRole.Id });
            await SeedProvisioningRowsAsync(db, demo.Id, isDemoProfileFilledIn: true);
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedProvisioningRowsAsync(MerfitDbContext db, Guid userId, bool isDemoProfileFilledIn = false)
    {
        await db.UserProfiles.AddAsync(isDemoProfileFilledIn
            ? new UserProfile
            {
                UserId = userId,
                HeightCm = 180,
                WeightKg = 78.4m,
                StartingWeightKg = 84m,
                TargetWeightKg = 75m,
                Goal = Domain.Enums.Goal.BuildMuscle,
                ActivityLevel = Domain.Enums.ActivityLevel.Moderate,
                TrainingExperience = Domain.Enums.TrainingExperience.Beginner,
                TrainingDaysPerWeek = 4,
                TrainingLocation = Domain.Enums.TrainingLocation.Home,
                IsCompleted = true,
                CompletedAt = DateTimeOffset.UtcNow,
            }
            : new UserProfile { UserId = userId });

        await db.UserSettings.AddAsync(new UserSettings { UserId = userId });
        await db.NotificationPreferences.AddAsync(new NotificationPreference { UserId = userId });
        await db.UserWorkoutStreaks.AddAsync(new UserWorkoutStreak { UserId = userId });
        await db.UserScores.AddAsync(new Domain.Entities.Scoring.UserScore { UserId = userId });
    }
}
