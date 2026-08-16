using Merfit.Domain.Entities.Nutrition;
using Merfit.Domain.Entities.Rewards;
using Merfit.Domain.Entities.Workouts;
using Merfit.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Merfit.Infrastructure.Persistence.Seed;

internal static class WorkoutSeedData
{
    public static async Task SeedAsync(MerfitDbContext db)
    {
        var categories = await SeedCategoriesAsync(db);
        var exercises = await SeedExercisesAsync(db);
        await SeedWorkoutsAsync(db, categories, exercises);
        await SeedRewardsAsync(db);
        await SeedFoodsAsync(db);
    }

    private static async Task<Dictionary<string, WorkoutCategory>> SeedCategoriesAsync(MerfitDbContext db)
    {
        var names = new[] { "Strength", "Cardio", "HIIT", "Mobility", "Core", "Upper Body", "Lower Body" };

        foreach (var (name, index) in names.Select((n, i) => (n, i)))
        {
            var slug = name.ToLowerInvariant().Replace(" ", "-");
            if (!await db.WorkoutCategories.AnyAsync(c => c.Slug == slug))
            {
                await db.WorkoutCategories.AddAsync(new WorkoutCategory { Name = name, Slug = slug, SortOrder = index });
            }
        }
        await db.SaveChangesAsync();

        return await db.WorkoutCategories.ToDictionaryAsync(c => c.Slug);
    }

    private static async Task<Dictionary<string, Exercise>> SeedExercisesAsync(MerfitDbContext db)
    {
        var defs = new (string Name, MuscleGroup MuscleGroup, Equipment Equipment, DifficultyLevel Difficulty)[]
        {
            ("Squat", MuscleGroup.Legs, Equipment.Barbell, DifficultyLevel.Intermediate),
            ("Bench Press", MuscleGroup.Chest, Equipment.Barbell, DifficultyLevel.Intermediate),
            ("Deadlift", MuscleGroup.Back, Equipment.Barbell, DifficultyLevel.Advanced),
            ("Push Up", MuscleGroup.Chest, Equipment.None, DifficultyLevel.Beginner),
            ("Plank", MuscleGroup.Core, Equipment.None, DifficultyLevel.Beginner),
            ("Lunge", MuscleGroup.Legs, Equipment.Dumbbells, DifficultyLevel.Beginner),
            ("Pull Up", MuscleGroup.Back, Equipment.PullUpBar, DifficultyLevel.Advanced),
            ("Burpee", MuscleGroup.FullBody, Equipment.None, DifficultyLevel.Intermediate),
            ("Mountain Climber", MuscleGroup.Core, Equipment.None, DifficultyLevel.Beginner),
            ("Dumbbell Bicep Curl", MuscleGroup.Arms, Equipment.Dumbbells, DifficultyLevel.Beginner),
            ("Kettlebell Swing", MuscleGroup.FullBody, Equipment.Kettlebell, DifficultyLevel.Intermediate),
            ("Shoulder Press", MuscleGroup.Shoulders, Equipment.Dumbbells, DifficultyLevel.Intermediate),
            ("Jumping Jacks", MuscleGroup.Cardio, Equipment.None, DifficultyLevel.Beginner),
            ("Glute Bridge", MuscleGroup.Glutes, Equipment.None, DifficultyLevel.Beginner),
            ("Resistance Band Row", MuscleGroup.Back, Equipment.Bands, DifficultyLevel.Beginner),
        };

        foreach (var d in defs)
        {
            if (!await db.Exercises.AnyAsync(e => e.Name == d.Name))
            {
                await db.Exercises.AddAsync(new Exercise
                {
                    Name = d.Name,
                    MuscleGroup = d.MuscleGroup,
                    Equipment = d.Equipment,
                    Difficulty = d.Difficulty,
                    ImageUrl = null,
                    VideoUrl = null,
                });
            }
        }
        await db.SaveChangesAsync();

        return await db.Exercises.ToDictionaryAsync(e => e.Name);
    }

    private static async Task SeedWorkoutsAsync(MerfitDbContext db, Dictionary<string, WorkoutCategory> categories, Dictionary<string, Exercise> exercises)
    {
        if (await db.Workouts.AnyAsync())
        {
            return;
        }

        var workouts = new[]
        {
            new Workout
            {
                Title = "Full Body Strength",
                Tagline = "Build strength across every major muscle group.",
                DurationMinutes = 45,
                Difficulty = DifficultyLevel.Intermediate,
                CategoryId = categories["strength"].Id,
                MuscleGroup = MuscleGroup.FullBody,
                Featured = true,
                Exercises =
                [
                    new WorkoutExercise { ExerciseId = exercises["Squat"].Id, SortOrder = 1, Sets = 4, Reps = 8, RestSeconds = 90 },
                    new WorkoutExercise { ExerciseId = exercises["Bench Press"].Id, SortOrder = 2, Sets = 4, Reps = 8, RestSeconds = 90 },
                    new WorkoutExercise { ExerciseId = exercises["Deadlift"].Id, SortOrder = 3, Sets = 3, Reps = 6, RestSeconds = 120 },
                ],
                Equipment = [new WorkoutEquipment { Equipment = Equipment.Barbell }],
            },
            new Workout
            {
                Title = "Upper Body Power",
                Tagline = "Push your chest, shoulders and arms to the limit.",
                DurationMinutes = 35,
                Difficulty = DifficultyLevel.Intermediate,
                CategoryId = categories["upper-body"].Id,
                MuscleGroup = MuscleGroup.UpperBody,
                Exercises =
                [
                    new WorkoutExercise { ExerciseId = exercises["Bench Press"].Id, SortOrder = 1, Sets = 4, Reps = 10, RestSeconds = 75 },
                    new WorkoutExercise { ExerciseId = exercises["Shoulder Press"].Id, SortOrder = 2, Sets = 3, Reps = 10, RestSeconds = 75 },
                    new WorkoutExercise { ExerciseId = exercises["Dumbbell Bicep Curl"].Id, SortOrder = 3, Sets = 3, Reps = 12, RestSeconds = 60 },
                ],
                Equipment = [new WorkoutEquipment { Equipment = Equipment.Dumbbells }, new WorkoutEquipment { Equipment = Equipment.Barbell }],
            },
            new Workout
            {
                Title = "Quick HIIT Blast",
                Tagline = "A fast, no-equipment cardio finisher.",
                DurationMinutes = 15,
                Difficulty = DifficultyLevel.Beginner,
                CategoryId = categories["hiit"].Id,
                MuscleGroup = MuscleGroup.FullBody,
                Featured = true,
                Exercises =
                [
                    new WorkoutExercise { ExerciseId = exercises["Jumping Jacks"].Id, SortOrder = 1, Sets = 3, Reps = 20, RestSeconds = 30 },
                    new WorkoutExercise { ExerciseId = exercises["Burpee"].Id, SortOrder = 2, Sets = 3, Reps = 12, RestSeconds = 30 },
                    new WorkoutExercise { ExerciseId = exercises["Mountain Climber"].Id, SortOrder = 3, Sets = 3, Reps = 20, RestSeconds = 30 },
                ],
                Equipment = [new WorkoutEquipment { Equipment = Equipment.None }],
            },
            new Workout
            {
                Title = "Core Crusher",
                Tagline = "Tighten and strengthen your core.",
                DurationMinutes = 20,
                Difficulty = DifficultyLevel.Beginner,
                CategoryId = categories["core"].Id,
                MuscleGroup = MuscleGroup.Core,
                Exercises =
                [
                    new WorkoutExercise { ExerciseId = exercises["Plank"].Id, SortOrder = 1, Sets = 3, Reps = 1, DurationSeconds = 45, RestSeconds = 30 },
                    new WorkoutExercise { ExerciseId = exercises["Mountain Climber"].Id, SortOrder = 2, Sets = 3, Reps = 20, RestSeconds = 30 },
                    new WorkoutExercise { ExerciseId = exercises["Glute Bridge"].Id, SortOrder = 3, Sets = 3, Reps = 15, RestSeconds = 30 },
                ],
                Equipment = [new WorkoutEquipment { Equipment = Equipment.None }],
            },
            new Workout
            {
                Title = "Lower Body Burn",
                Tagline = "Legs and glutes, no gym required.",
                DurationMinutes = 30,
                Difficulty = DifficultyLevel.Beginner,
                CategoryId = categories["lower-body"].Id,
                MuscleGroup = MuscleGroup.LowerBody,
                Exercises =
                [
                    new WorkoutExercise { ExerciseId = exercises["Lunge"].Id, SortOrder = 1, Sets = 3, Reps = 12, RestSeconds = 60 },
                    new WorkoutExercise { ExerciseId = exercises["Glute Bridge"].Id, SortOrder = 2, Sets = 3, Reps = 15, RestSeconds = 45 },
                    new WorkoutExercise { ExerciseId = exercises["Kettlebell Swing"].Id, SortOrder = 3, Sets = 3, Reps = 15, RestSeconds = 60 },
                ],
                Equipment = [new WorkoutEquipment { Equipment = Equipment.Dumbbells }, new WorkoutEquipment { Equipment = Equipment.Kettlebell }],
            },
            new Workout
            {
                Title = "Mobility & Recovery",
                Tagline = "Move better and recover faster.",
                DurationMinutes = 20,
                Difficulty = DifficultyLevel.Beginner,
                CategoryId = categories["mobility"].Id,
                MuscleGroup = MuscleGroup.FullBody,
                Exercises =
                [
                    new WorkoutExercise { ExerciseId = exercises["Plank"].Id, SortOrder = 1, Sets = 2, Reps = 1, DurationSeconds = 30, RestSeconds = 30 },
                    new WorkoutExercise { ExerciseId = exercises["Glute Bridge"].Id, SortOrder = 2, Sets = 2, Reps = 12, RestSeconds = 30 },
                ],
                Equipment = [new WorkoutEquipment { Equipment = Equipment.None }],
            },
            new Workout
            {
                Title = "Back & Pull Day",
                Tagline = "Build a stronger back and biceps.",
                DurationMinutes = 40,
                Difficulty = DifficultyLevel.Advanced,
                CategoryId = categories["strength"].Id,
                MuscleGroup = MuscleGroup.Back,
                Exercises =
                [
                    new WorkoutExercise { ExerciseId = exercises["Deadlift"].Id, SortOrder = 1, Sets = 4, Reps = 6, RestSeconds = 120 },
                    new WorkoutExercise { ExerciseId = exercises["Pull Up"].Id, SortOrder = 2, Sets = 4, Reps = 8, RestSeconds = 90 },
                    new WorkoutExercise { ExerciseId = exercises["Resistance Band Row"].Id, SortOrder = 3, Sets = 3, Reps = 15, RestSeconds = 60 },
                ],
                Equipment = [new WorkoutEquipment { Equipment = Equipment.Barbell }, new WorkoutEquipment { Equipment = Equipment.PullUpBar }],
            },
            new Workout
            {
                Title = "Cardio Endurance",
                Tagline = "Build your engine with steady-state cardio intervals.",
                DurationMinutes = 25,
                Difficulty = DifficultyLevel.Intermediate,
                CategoryId = categories["cardio"].Id,
                MuscleGroup = MuscleGroup.Cardio,
                Exercises =
                [
                    new WorkoutExercise { ExerciseId = exercises["Jumping Jacks"].Id, SortOrder = 1, Sets = 4, Reps = 30, RestSeconds = 30 },
                    new WorkoutExercise { ExerciseId = exercises["Burpee"].Id, SortOrder = 2, Sets = 4, Reps = 15, RestSeconds = 45 },
                ],
                Equipment = [new WorkoutEquipment { Equipment = Equipment.None }],
            },
        };

        await db.Workouts.AddRangeAsync(workouts);
        await db.SaveChangesAsync();
    }

    private static async Task SeedRewardsAsync(MerfitDbContext db)
    {
        if (await db.Rewards.AnyAsync())
        {
            return;
        }

        var rewards = new (int Rank, string Title, string Description, RewardType Type)[]
        {
            (1, "Smartwatch", "A premium fitness smartwatch.", RewardType.Merchandise),
            (2, "12 Months Plus", "A full year of Merfit Plus, on us.", RewardType.SubscriptionVoucher),
            (3, "6 Months Plus", "Half a year of Merfit Plus.", RewardType.SubscriptionVoucher),
            (4, "Gym Bag", "A premium branded gym bag.", RewardType.Merchandise),
            (5, "Shoes Voucher", "A voucher toward training shoes.", RewardType.Discount),
            (6, "Personal Training Session", "One session with a certified trainer.", RewardType.Other),
            (7, "Protein Shaker", "A Merfit-branded protein shaker.", RewardType.Merchandise),
            (8, "Apparel Voucher", "A voucher for training apparel.", RewardType.Discount),
            (9, "1 Month Plus", "One month of Merfit Plus.", RewardType.SubscriptionVoucher),
            (10, "Membership Voucher", "A discount voucher for your next renewal.", RewardType.Discount),
        };

        foreach (var (rank, title, description, type) in rewards)
        {
            await db.Rewards.AddAsync(new Reward
            {
                Rank = rank,
                Title = title,
                Description = description,
                RewardType = type,
                Value = type == RewardType.SubscriptionVoucher ? "plus_voucher" : "physical_item",
                Period = LeaderboardPeriod.Weekly,
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedFoodsAsync(MerfitDbContext db)
    {
        if (await db.Foods.AnyAsync())
        {
            return;
        }

        var foods = new (string Name, decimal Cal, decimal Protein, decimal Carbs, decimal Fat)[]
        {
            ("Chicken Breast (cooked)", 165, 31, 0, 3.6m),
            ("White Rice (cooked)", 130, 2.7m, 28, 0.3m),
            ("Broccoli", 34, 2.8m, 7, 0.4m),
            ("Egg", 155, 13, 1.1m, 11),
            ("Oatmeal", 68, 2.4m, 12, 1.4m),
            ("Greek Yogurt", 59, 10, 3.6m, 0.4m),
            ("Banana", 89, 1.1m, 23, 0.3m),
            ("Almonds", 579, 21, 22, 50),
            ("Salmon (cooked)", 208, 20, 0, 13),
            ("Sweet Potato", 86, 1.6m, 20, 0.1m),
            ("Whole Wheat Bread", 247, 13, 41, 3.4m),
            ("Avocado", 160, 2, 8.5m, 14.7m),
            ("Peanut Butter", 588, 25, 20, 50),
            ("Whey Protein Powder", 400, 80, 8, 5),
            ("Apple", 52, 0.3m, 14, 0.2m),
        };

        foreach (var (name, cal, protein, carbs, fat) in foods)
        {
            await db.Foods.AddAsync(new Food
            {
                Name = name,
                CaloriesPer100g = cal,
                ProteinPer100g = protein,
                CarbsPer100g = carbs,
                FatPer100g = fat,
                IsVerified = true,
                Source = FoodSource.System,
            });
        }
        await db.SaveChangesAsync();
    }
}
