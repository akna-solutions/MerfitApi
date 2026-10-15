using MerfitApi.Business.Dtos.Admin.Workouts;
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
/// Admin puanlama: plan uretimi yalnizca Hipertrofi/Kuvvet/Kardiyo puanlarini kullanir; bunlar birlikte
/// zorunludur ve eksikleri yanitta isaretlenir. Antrenman sonu kardiyo olarak kullanilan antrenman silinemez.
/// </summary>
public class AdminWorkoutAttributeScoreTests
{
    private const long WorkoutId = 5;

    private static (AdminWorkoutService Service, InMemoryUnitOfWork Uow) Create()
    {
        var uow = new InMemoryUnitOfWork();
        uow.Set<Workout>().Add(new Workout { Id = WorkoutId, Title = "HIIT", Slug = "hiit", IsActive = true });
        uow.Set<WorkoutAttributeScore>();
        uow.Set<WorkoutPlanDay>();
        uow.Set<WorkoutSession>();
        uow.Set<WorkoutExercise>();
        uow.Set<WorkoutEquipment>();
        return (new AdminWorkoutService(uow.Object, Mock.Of<IAuditLogService>()), uow);
    }

    private static AdminSetWorkoutAttributeScoresRequest Request(params (WorkoutAttributeType Type, int Score)[] items) => new()
    {
        Scores = items.Select(i => new AdminWorkoutAttributeScoreInput { AttributeType = i.Type, Score = i.Score }).ToList(),
    };

    [Fact]
    public async Task PartialPrimaryScores_AreRejected()
    {
        var (service, uow) = Create();

        await Assert.ThrowsAsync<AppValidationException>(() => service.SetAttributeScoresAsync(
            WorkoutId, Request((WorkoutAttributeType.Hypertrophy, 90), (WorkoutAttributeType.Mobility, 50)), 1, null, null));

        Assert.Empty(uow.Set<WorkoutAttributeScore>());
    }

    [Fact]
    public async Task AllPrimaryScores_AreSaved_AndSecondaryStayOptional()
    {
        var (service, _) = Create();

        var result = await service.SetAttributeScoresAsync(
            WorkoutId,
            Request((WorkoutAttributeType.Hypertrophy, 20), (WorkoutAttributeType.Strength, 15), (WorkoutAttributeType.Cardio, 95)),
            1, null, null);

        Assert.True(result.IsPrimaryComplete);
        Assert.Empty(result.MissingPrimaryAttributeTypes);
        Assert.Equal(new[] { "Endurance", "Mobility", "CoreStability" }, result.MissingAttributeTypes);
    }

    [Fact]
    public async Task UnscoredWorkout_ReportsMissingPrimaryScores()
    {
        var (service, _) = Create();

        var result = await service.GetAttributeScoresAsync(WorkoutId);

        Assert.False(result.IsPrimaryComplete);
        Assert.Equal(new[] { "Hypertrophy", "Strength", "Cardio" }, result.MissingPrimaryAttributeTypes);
    }

    [Fact]
    public async Task WorkoutUsedAsCardioFinisher_CannotBeDeleted()
    {
        var (service, uow) = Create();
        uow.Set<WorkoutPlanDay>().Add(new WorkoutPlanDay { Id = 1, WorkoutPlanId = 1, WorkoutId = 99, CardioWorkoutId = WorkoutId });

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(WorkoutId, 1, null, null));
    }
}
