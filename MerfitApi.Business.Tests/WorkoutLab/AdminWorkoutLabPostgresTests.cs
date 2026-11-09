using MerfitApi.Business.Dtos.Admin.WorkoutLab;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Business.Services.Admin;
using MerfitApi.Business.Tests.Notifications;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.WorkoutLab;

/// <summary>
/// Gercek PostgreSQL'de (MERFIT_TEST_POSTGRES; sema MerfitCustomerApi migration'lariyla kurulmus olmali):
/// olusturma, liste sorgusu (admin e-postasi alt sorgusu), jsonb okuma ve degerlendirme SQL'e cevrilebiliyor.
/// </summary>
public class AdminWorkoutLabPostgresTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationJobStoreTests.ConnectionVariable)).Options);

    [PostgresFact]
    public async Task CreateListEvaluate_RunAgainstRealDatabase()
    {
        await using var db = CreateContext();
        var service = new AdminWorkoutLabService(new UnitOfWork(db), Mock.Of<IAuditLogService>());
        var name = $"pg-{Guid.NewGuid():N}";

        var created = await service.CreateAsync(new AdminCreateWorkoutLabSimulationRequest
        {
            Name = name,
            Profile = new AdminWorkoutLabProfileInput { Goal = FitnessGoal.GetStronger, TrainingLocation = TrainingLocation.Gym, TrainingDays = new() { "Mon", "Wed" } },
        }, null, null, null);

        var list = await service.GetListAsync(new AdminWorkoutLabSimulationListRequest { Search = name, Goal = FitnessGoal.GetStronger });
        Assert.Equal(created.Id, list.Items.Single().Id);
        Assert.Equal("Pending", list.Items.Single().Status);
        Assert.Equal(0, list.Items.Single().TrainingDaysPerWeek);

        var detail = await service.GetByIdAsync(created.Id);
        Assert.Equal("Gym", detail.Profile!.Value.GetProperty("trainingLocation").GetString());

        // Isleyicinin yaptigi gibi tamamla, sonra degerlendir (xmin concurrency token ile).
        await db.WorkoutPlanSimulations.Where(s => s.Id == created.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, WorkoutPlanSimulationStatus.Completed).SetProperty(x => x.ResultJson, "{\"days\":[]}"));
        db.ChangeTracker.Clear();

        var evaluated = await service.EvaluateAsync(created.Id, new AdminEvaluateWorkoutLabSimulationRequest
        {
            Rating = WorkoutPlanEvaluationRating.VeryGood,
            ProblemItems = new() { new AdminWorkoutLabProblemItem { DayOfWeek = "Mon", Note = "ok" } },
        }, null, null, null);
        Assert.Equal("VeryGood", evaluated.Rating);
        Assert.Single(evaluated.Evaluation!.ProblemItems);

        await service.DeleteAsync(created.Id, null, null, null);
        Assert.False(await db.WorkoutPlanSimulations.AnyAsync(s => s.Id == created.Id));
    }
}
