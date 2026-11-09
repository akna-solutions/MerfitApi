using System.Text.Json;
using MerfitApi.Business.Dtos.Admin.WorkoutLab;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Business.Services.Admin;
using MerfitApi.Business.Tests.TestHelpers;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.WorkoutLab;

/// <summary>
/// Antrenman Laboratuvari admin servisi: profil mobil kurallarina gore normalize edilir, kayit Pending acilir
/// (program MerfitCustomerApi'de uretilir), kullanici tablolarina yazilmaz; tekrar calistirma, degerlendirme,
/// filtreleme ve karsilastirma.
/// </summary>
public class AdminWorkoutLabServiceTests
{
    private static (AdminWorkoutLabService Service, InMemoryUnitOfWork Uow, Mock<IAuditLogService> Audit) Create()
    {
        var uow = new InMemoryUnitOfWork();
        uow.Set<Equipment>().AddRange(new[]
        {
            new Equipment { Id = 1, Name = "Dumbbell", Slug = "dumbbell" },
            new Equipment { Id = 2, Name = "Barbell", Slug = "barbell" },
        });
        uow.Set<Workout>().AddRange(new[]
        {
            new Workout { Id = 1, Title = "A", Slug = "a", IsActive = true },
            new Workout { Id = 2, Title = "B", Slug = "b", IsActive = true, IsPremium = true },
            new Workout { Id = 3, Title = "C", Slug = "c", IsActive = false },
        });
        uow.Set<WorkoutAttributeScore>().Add(new WorkoutAttributeScore { Id = 1, WorkoutId = 1, AttributeType = WorkoutAttributeType.Hypertrophy, Score = 50 });
        uow.Set<WorkoutPlanSimulation>();
        uow.Set<ApplicationUser>().Add(new ApplicationUser { Id = 9, Email = "admin@test.local", Role = UserRole.Admin });
        // Kullanici tablolari: hic yazilmamali.
        uow.Register<UserProfile>().Register<WorkoutPlan>().Register<PersonalizationJob>();
        var audit = new Mock<IAuditLogService>();
        return (new AdminWorkoutLabService(uow.Object, audit.Object), uow, audit);
    }

    private static AdminCreateWorkoutLabSimulationRequest Request(Action<AdminWorkoutLabProfileInput>? configure = null)
    {
        var profile = new AdminWorkoutLabProfileInput
        {
            Age = 30, Gender = Gender.Male, HeightCm = 180, WeightKg = 80, TargetWeightKg = 75,
            Goal = FitnessGoal.BuildMuscle, ExperienceLevel = ExperienceLevel.Beginner, TrainingLocation = TrainingLocation.Home,
            HasEquipment = true, WorkoutDurationMin = 45,
        };
        configure?.Invoke(profile);
        return new AdminCreateWorkoutLabSimulationRequest { Name = " Test ", Profile = profile };
    }

    private static JsonElement Profile(InMemoryUnitOfWork uow, long id) =>
        JsonDocument.Parse(uow.Set<WorkoutPlanSimulation>().Single(s => s.Id == id).ProfileJson).RootElement;

    [Fact]
    public async Task Create_StoresPendingSimulation_WithCustomerApiProfileContract_AndTouchesNoUserData()
    {
        var (service, uow, audit) = Create();

        var dto = await service.CreateAsync(Request(), 9, null, null);

        Assert.Equal("Pending", dto.Status);
        Assert.Equal("Test", dto.Name);
        Assert.Equal("admin@test.local", dto.CreatedByEmail);
        var sim = uow.Set<WorkoutPlanSimulation>().Single();
        Assert.Equal(WorkoutPlanSimulationStatus.Pending, sim.Status);
        Assert.Equal(9, sim.CreatedUser);
        var json = Profile(uow, sim.Id);
        Assert.Equal("BuildMuscle", json.GetProperty("goal").GetString());
        Assert.Equal("Beginner", json.GetProperty("experienceLevel").GetString());
        Assert.Equal("Home", json.GetProperty("trainingLocation").GetString());
        Assert.True(json.GetProperty("hasEquipment").GetBoolean());
        Assert.False(json.GetProperty("isPlus").GetBoolean());
        Assert.Empty(uow.Set<UserProfile>());
        Assert.Empty(uow.Set<WorkoutPlan>());
        Assert.Empty(uow.Set<PersonalizationJob>());
        audit.Verify(a => a.LogAsync(9, "WORKOUT_LAB_SIMULATION_CREATED", nameof(WorkoutPlanSimulation), sim.Id, null, null, null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(FitnessGoal.MaintainWeight)]
    [InlineData(FitnessGoal.ImproveEndurance)]
    [InlineData(FitnessGoal.ImproveFitness)]
    public async Task LegacyGoals_AreRejected(FitnessGoal goal)
    {
        var (service, uow, _) = Create();

        await Assert.ThrowsAsync<AppValidationException>(() => service.CreateAsync(Request(p => p.Goal = goal), 9, null, null));
        Assert.Empty(uow.Set<WorkoutPlanSimulation>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EquipmentFlag_IsStoredAsIs_WithoutEquipmentList(bool hasEquipment)
    {
        var (service, uow, _) = Create();

        var dto = await service.CreateAsync(Request(p => p.HasEquipment = hasEquipment), 9, null, null);

        var json = Profile(uow, dto.Id);
        Assert.Equal(hasEquipment, json.GetProperty("hasEquipment").GetBoolean());
        Assert.Equal(!hasEquipment, json.GetProperty("noEquipment").GetBoolean());
        Assert.Equal(0, json.GetProperty("equipmentIds").GetArrayLength());
    }

    [Fact]
    public async Task Gym_IsAlwaysEquipped()
    {
        var (service, uow, _) = Create();

        var dto = await service.CreateAsync(Request(p => { p.TrainingLocation = TrainingLocation.Gym; p.HasEquipment = false; }), 9, null, null);

        Assert.True(Profile(uow, dto.Id).GetProperty("hasEquipment").GetBoolean());
    }

    [Theory]
    [InlineData(false, new long[] { 1 }, true)]
    [InlineData(true, new long[] { 1 }, false)]
    [InlineData(false, new long[0], false)]
    public async Task LegacyClients_WithoutFlag_DeriveItFromEquipmentList(bool noEquipment, long[] ids, bool expected)
    {
        var (service, uow, _) = Create();

        var dto = await service.CreateAsync(Request(p => { p.HasEquipment = null; p.NoEquipment = noEquipment; p.EquipmentIds = ids.ToList(); }), 9, null, null);

        Assert.Equal(expected, Profile(uow, dto.Id).GetProperty("hasEquipment").GetBoolean());
    }

    [Fact]
    public async Task AvailableDays_AreSortedMondayFirst_AndDayCountIsLeftToAlgorithm()
    {
        var (service, uow, _) = Create();

        var dto = await service.CreateAsync(Request(p => { p.TrainingDaysPerWeek = 5; p.TrainingDays = new List<string> { "sat", "Mon", "Thu", "Mon" }; }), 9, null, null);

        var json = Profile(uow, dto.Id);
        Assert.Equal(new[] { "Mon", "Thu", "Sat" }, json.GetProperty("trainingDays").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("trainingDaysPerWeek").ValueKind);
        Assert.Equal(0, dto.TrainingDaysPerWeek);
    }

    [Theory]
    [InlineData("Mon")]
    [InlineData("Funday")]
    public async Task InvalidDaySelections_AreRejected(string day)
    {
        var (service, _, _) = Create();

        await Assert.ThrowsAsync<AppValidationException>(() => service.CreateAsync(Request(p => p.TrainingDays = new List<string> { day }), 9, null, null));
    }

    [Fact]
    public async Task EmptyOptionalFields_StayEmptyForAlgorithmDefaults()
    {
        var (service, uow, _) = Create();

        var dto = await service.CreateAsync(Request(p => { p.Goal = null; p.ExperienceLevel = null; p.TrainingDaysPerWeek = null; p.WorkoutDurationMin = null; }), 9, null, null);

        var json = Profile(uow, dto.Id);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("goal").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("workoutDurationMin").ValueKind);
        Assert.Equal("BuildMuscle", dto.Goal);
    }

    [Fact]
    public async Task Rerun_CopiesProfile_AndLinksSource()
    {
        var (service, uow, _) = Create();
        var first = await service.CreateAsync(Request(p => p.IsPlus = true), 9, null, null);
        var original = uow.Set<WorkoutPlanSimulation>().Single();
        original.Status = WorkoutPlanSimulationStatus.Completed;
        original.AlgorithmVersion = "v1";

        var rerun = await service.RerunAsync(first.Id, 9, null, null);

        Assert.Equal("Pending", rerun.Status);
        Assert.Equal(first.Id, rerun.SourceSimulationId);
        Assert.Null(rerun.AlgorithmVersion);
        Assert.Equal(original.ProfileJson, uow.Set<WorkoutPlanSimulation>().Single(s => s.Id == rerun.Id).ProfileJson);
        Assert.True(rerun.IsPlus);
    }

    [Fact]
    public async Task Evaluate_RequiresProcessedSimulation_AndStoresAllFields()
    {
        var (service, uow, _) = Create();
        var dto = await service.CreateAsync(Request(), 9, null, null);

        var request = new AdminEvaluateWorkoutLabSimulationRequest
        {
            Rating = WorkoutPlanEvaluationRating.NeedsImprovement,
            MissingPoints = " Bacak hacmi az ",
            ProblemItems = new List<AdminWorkoutLabProblemItem> { new() { DayOfWeek = "Wed", ExerciseId = 5, ExerciseName = "Squat", Note = "Tekrar" } },
            ExerciseSuggestions = "Bulgarian split squat",
            Comment = "Genel olarak iyi",
        };

        await Assert.ThrowsAsync<AppValidationException>(() => service.EvaluateAsync(dto.Id, request, 9, null, null));

        uow.Set<WorkoutPlanSimulation>().Single().Status = WorkoutPlanSimulationStatus.Completed;
        var evaluated = await service.EvaluateAsync(dto.Id, request, 9, null, null);

        Assert.Equal("NeedsImprovement", evaluated.Rating);
        Assert.Equal("Bacak hacmi az", evaluated.Evaluation!.MissingPoints);
        Assert.Equal("Wed", evaluated.Evaluation.ProblemItems.Single().DayOfWeek);
        Assert.Equal(5, evaluated.Evaluation.ProblemItems.Single().ExerciseId);
        Assert.Equal("Bulgarian split squat", evaluated.Evaluation.ExerciseSuggestions);
        Assert.Equal("admin@test.local", evaluated.Evaluation.EvaluatedByEmail);
    }

    [Fact]
    public async Task Evaluate_RejectsInvalidDay()
    {
        var (service, uow, _) = Create();
        var dto = await service.CreateAsync(Request(), 9, null, null);
        uow.Set<WorkoutPlanSimulation>().Single().Status = WorkoutPlanSimulationStatus.Failed;

        await Assert.ThrowsAsync<AppValidationException>(() => service.EvaluateAsync(dto.Id, new AdminEvaluateWorkoutLabSimulationRequest
        {
            Rating = WorkoutPlanEvaluationRating.Poor,
            ProblemItems = new List<AdminWorkoutLabProblemItem> { new() { DayOfWeek = "Monday" } },
        }, 9, null, null));
    }

    [Fact]
    public async Task List_FiltersByStatusGoalRatingVersionAndFailures()
    {
        var (service, uow, _) = Create();
        var sims = uow.Set<WorkoutPlanSimulation>();
        sims.AddRange(new[]
        {
            new WorkoutPlanSimulation { Id = 1, Status = WorkoutPlanSimulationStatus.Completed, Goal = FitnessGoal.BuildMuscle, AlgorithmVersion = "v1", FailedCheckCount = 1, Rating = WorkoutPlanEvaluationRating.Good, ProfileJson = "{}", CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new WorkoutPlanSimulation { Id = 2, Status = WorkoutPlanSimulationStatus.Completed, Goal = FitnessGoal.LoseWeight, AlgorithmVersion = "v2", ProfileJson = "{}", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new WorkoutPlanSimulation { Id = 3, Status = WorkoutPlanSimulationStatus.Failed, Goal = FitnessGoal.LoseWeight, AlgorithmVersion = "v2", ProfileJson = "{}", CreatedAt = DateTime.UtcNow, Name = "ev testi" },
        });

        Assert.Equal(new long[] { 3, 2, 1 }, (await service.GetListAsync(new())).Items.Select(i => i.Id));
        Assert.Equal(new long[] { 2, 1 }, (await service.GetListAsync(new() { Status = WorkoutPlanSimulationStatus.Completed })).Items.Select(i => i.Id));
        Assert.Equal(new long[] { 3, 2 }, (await service.GetListAsync(new() { Goal = FitnessGoal.LoseWeight })).Items.Select(i => i.Id));
        Assert.Equal(new long[] { 1 }, (await service.GetListAsync(new() { HasFailedChecks = true })).Items.Select(i => i.Id));
        Assert.Equal(new long[] { 1 }, (await service.GetListAsync(new() { IsEvaluated = true })).Items.Select(i => i.Id));
        Assert.Equal(new long[] { 3, 2 }, (await service.GetListAsync(new() { AlgorithmVersion = "v2" })).Items.Select(i => i.Id));
        Assert.Equal(new long[] { 3 }, (await service.GetListAsync(new() { Search = "ev" })).Items.Select(i => i.Id));
        Assert.Equal(new long[] { 1 }, (await service.GetListAsync(new() { Rating = WorkoutPlanEvaluationRating.Good })).Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Compare_RequiresTwoCompletedSimulations_AndDetectsSameProfile()
    {
        var (service, uow, _) = Create();
        var sims = uow.Set<WorkoutPlanSimulation>();
        const string profile = "{\"goal\":\"BuildMuscle\",\"trainingDaysPerWeek\":3}";
        sims.AddRange(new[]
        {
            new WorkoutPlanSimulation { Id = 1, Status = WorkoutPlanSimulationStatus.Completed, ProfileJson = profile, ResultJson = "{\"days\":[]}", AlgorithmVersion = "v1", OptionsFingerprint = "a" },
            new WorkoutPlanSimulation { Id = 2, Status = WorkoutPlanSimulationStatus.Completed, ProfileJson = "{ \"trainingDaysPerWeek\": 3, \"goal\": \"BuildMuscle\" }", ResultJson = "{\"days\":[]}", AlgorithmVersion = "v2", OptionsFingerprint = "a" },
            new WorkoutPlanSimulation { Id = 3, Status = WorkoutPlanSimulationStatus.Pending, ProfileJson = profile },
        });

        var comparison = await service.CompareAsync(1, 2);

        Assert.True(comparison.SameProfile);
        Assert.False(comparison.SameAlgorithmVersion);
        Assert.True(comparison.SameOptionsFingerprint);
        await Assert.ThrowsAsync<AppValidationException>(() => service.CompareAsync(1, 3));
        await Assert.ThrowsAsync<AppValidationException>(() => service.CompareAsync(1, 1));
        await Assert.ThrowsAsync<NotFoundException>(() => service.CompareAsync(1, 99));
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheSimulation_ButNotWhileProcessing()
    {
        var (service, uow, _) = Create();
        var sims = uow.Set<WorkoutPlanSimulation>();
        sims.Add(new WorkoutPlanSimulation { Id = 1, Status = WorkoutPlanSimulationStatus.Processing, ProfileJson = "{}" });
        sims.Add(new WorkoutPlanSimulation { Id = 2, Status = WorkoutPlanSimulationStatus.Completed, ProfileJson = "{}" });

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(1, 9, null, null));
        await service.DeleteAsync(2, 9, null, null);

        Assert.Equal(new long[] { 1 }, sims.Select(s => s.Id));
    }

    [Fact]
    public async Task Options_ReturnDynamicEquipmentCatalogAndCatalogStats()
    {
        var (service, uow, _) = Create();
        uow.Set<WorkoutPlanSimulation>().Add(new WorkoutPlanSimulation { Id = 1, Status = WorkoutPlanSimulationStatus.Pending, ProfileJson = "{}", CreatedAt = DateTime.UtcNow });

        var options = await service.GetOptionsAsync();

        Assert.Equal(new[] { "Barbell", "Dumbbell" }, options.Equipment.Select(e => e.Name));
        Assert.Equal(new[] { "BuildMuscle", "GetStronger", "LoseWeight" }, options.Goals.Select(g => g.Value));
        Assert.Equal(new[] { 15, 20, 30, 45, 60, 75, 90 }, options.DurationOptions);
        Assert.Equal(new[] { 3, 4, 5 }, options.TargetTrainingDaysByExperience.Values);
        Assert.Equal(1, options.LoseWeightExtraTrainingDays);
        Assert.Equal(2, options.ActiveWorkoutCount);
        Assert.Equal(1, options.ActivePremiumWorkoutCount);
        Assert.Equal(1, options.ActiveScoredWorkoutCount);
        Assert.Equal(1, options.PendingCount);
    }
}
