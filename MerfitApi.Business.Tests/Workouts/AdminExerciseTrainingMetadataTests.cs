using MerfitApi.Business.Dtos.Admin.Exercises;
using MerfitApi.Business.Dtos.Admin.WorkoutPlans;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Business.Services.Admin;
using MerfitApi.Business.Tests.TestHelpers;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.Workouts;

/// <summary>
/// Antrenman motoru icin mevcut egzersizlere metadata baglama (yeni egzersiz olusturmadan), eksik metadata raporu
/// ve motor programlarinin panelden gun bazinda ezilmemesi.
/// </summary>
public class AdminExerciseTrainingMetadataTests
{
    private static InMemoryUnitOfWork Seed()
    {
        var uow = new InMemoryUnitOfWork();
        uow.Set<MuscleGroup>().AddRange(new[]
        {
            new MuscleGroup { Id = 1, Name = "Göğüs", Slug = "chest" },
            new MuscleGroup { Id = 5, Name = "Triceps", Slug = "triceps" },
            new MuscleGroup { Id = 3, Name = "Omuz", Slug = "shoulders" },
        });
        uow.Set<Equipment>().AddRange(new[] { new Equipment { Id = 1, Name = "Barbell", Slug = "barbell" }, new Equipment { Id = 9, Name = "Bench", Slug = "bench" } });
        uow.Set<Exercise>().AddRange(new[]
        {
            new Exercise { Id = 10, Name = "Bench Press", Slug = "bench-press", PrimaryMuscleGroupId = 1, EquipmentType = "Barbell", IsActive = true, ImageUrl = "x" },
            new Exercise { Id = 11, Name = "Cable Fly", Slug = "cable-fly", PrimaryMuscleGroupId = 1, IsActive = true, MechanicType = ExerciseMechanicType.Isolation },
            new Exercise { Id = 12, Name = "Old", Slug = "old", PrimaryMuscleGroupId = 1, IsActive = false },
        });
        uow.Register<ExerciseMuscleGroup>().Register<ExerciseEquipment>();
        return uow;
    }

    private static AdminExerciseService Service(InMemoryUnitOfWork uow) => new(uow.Object, Mock.Of<IAuditLogService>());

    [Fact]
    public async Task UpdateTrainingMetadata_LinksSecondaryMusclesAndEquipment_WithoutCreatingExercises()
    {
        var uow = Seed();
        var dto = await Service(uow).UpdateTrainingMetadataAsync(10, new AdminUpdateExerciseTrainingMetadataRequest
        {
            MechanicType = ExerciseMechanicType.Compound,
            MovementPattern = MovementPattern.HorizontalPush,
            LoadIncrementKg = 2.5m,
            SecondaryMuscleGroupIds = new List<long> { 5, 3, 1 },
            EquipmentIds = new List<long> { 1, 9 },
        }, adminUserId: 1, null, null);

        Assert.Equal("Compound", dto.TrainingMetadata.MechanicType);
        Assert.Equal(new long[] { 3, 5 }, dto.TrainingMetadata.SecondaryMuscleGroupIds);
        Assert.Equal(new long[] { 1, 9 }, dto.TrainingMetadata.EquipmentIds);
        Assert.Equal(3, uow.Set<Exercise>().Count);
        // Ana kas yardimci olarak eklenmez.
        Assert.DoesNotContain(uow.Set<ExerciseMuscleGroup>(), l => l.MuscleGroupId == 1);

        // Ikinci guncelleme fazlaligi siler, yenisini ekler.
        await Service(uow).UpdateTrainingMetadataAsync(10, new AdminUpdateExerciseTrainingMetadataRequest
        {
            MechanicType = ExerciseMechanicType.Compound, SecondaryMuscleGroupIds = new List<long> { 5 }, EquipmentIds = new List<long> { 1 },
        }, 1, null, null);
        Assert.Equal(new long[] { 5 }, uow.Set<ExerciseMuscleGroup>().Select(l => l.MuscleGroupId));
        Assert.Equal(new long[] { 1 }, uow.Set<ExerciseEquipment>().Select(l => l.EquipmentId));
    }

    [Fact]
    public async Task UpdateTrainingMetadata_UnknownMuscleOrEquipment_IsRejected()
    {
        var uow = Seed();
        await Assert.ThrowsAsync<AppValidationException>(() => Service(uow).UpdateTrainingMetadataAsync(10,
            new AdminUpdateExerciseTrainingMetadataRequest { SecondaryMuscleGroupIds = new List<long> { 99 } }, 1, null, null));
        await Assert.ThrowsAsync<AppValidationException>(() => Service(uow).UpdateTrainingMetadataAsync(10,
            new AdminUpdateExerciseTrainingMetadataRequest { EquipmentIds = new List<long> { 99 } }, 1, null, null));
    }

    [Fact]
    public async Task MetadataAudit_CountsMissingFields_ForActiveExercisesOnly()
    {
        var uow = Seed();
        var audit = await Service(uow).GetMetadataAuditAsync();

        Assert.Equal(2, audit.ActiveExerciseCount);
        Assert.Equal(1, audit.MissingMechanicType);
        Assert.Equal(2, audit.MissingMovementPattern);
        // Izolasyonda yardimci kas beklenmez.
        Assert.Equal(1, audit.MissingSecondaryMuscles);
        Assert.Equal(2, audit.MissingEquipmentLinks);
        Assert.Equal(1, audit.MissingImageOrVideo);
        Assert.Equal(2, Assert.Single(audit.ByMuscleGroup).ActiveExerciseCount);
        Assert.DoesNotContain(audit.Items, i => i.Id == 12);
    }

    [Fact]
    public async Task SetDays_OnEngineProgram_IsRejected_ToPreserveHistory()
    {
        var uow = new InMemoryUnitOfWork();
        uow.Set<WorkoutPlan>().Add(new WorkoutPlan { Id = 1, UserId = 3, Name = "Program V1", Source = TrainingProgramSource.Engine, Status = TrainingProgramStatus.Active, IsActive = true });
        uow.Register<WorkoutPlanDay>().Register<Workout>();
        var service = new AdminWorkoutPlanService(uow.Object, Mock.Of<IAuditLogService>());

        await Assert.ThrowsAsync<AppValidationException>(() => service.SetDaysAsync(1, new AdminSetWorkoutPlanDaysRequest(), 1, null, null));
    }
}
