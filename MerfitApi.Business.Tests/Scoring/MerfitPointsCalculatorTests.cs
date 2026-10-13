using MerfitApi.Business.Common;
using MerfitApi.Business.Services.Admin;
using MerfitApi.Business.Tests.TestHelpers;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Interfaces;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.Scoring;

/// <summary>
/// Admin tarafindaki puan yeniden hesaplamasi (MerfitPointsCalculator), MerfitCustomerApi'deki puan motoruyla
/// ayni kurallari uygulamali; mini oyun odulleri de dahil. Aksi halde admin "yeniden hesapla" dediginde
/// kullanicinin mini oyundan kazandigi puanlar kaybolur.
/// </summary>
public class MerfitPointsCalculatorTests
{
    private const long UserA = 1;
    private const long UserB = 2;

    private readonly List<MiniGameSession> _miniGames = new();
    private readonly List<WorkoutSession> _sessions = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public MerfitPointsCalculatorTests()
    {
        _unitOfWork.Setup(u => u.Repository<WorkoutSession>()).Returns(MockRepositoryFactory.Create(_sessions).Object);
        _unitOfWork.Setup(u => u.Repository<PersonalRecord>()).Returns(MockRepositoryFactory.Create(new List<PersonalRecord>()).Object);
        _unitOfWork.Setup(u => u.Repository<Meal>()).Returns(MockRepositoryFactory.Create(new List<Meal>()).Object);
        _unitOfWork.Setup(u => u.Repository<MealItem>()).Returns(MockRepositoryFactory.Create(new List<MealItem>()).Object);
        _unitOfWork.Setup(u => u.Repository<WaterLog>()).Returns(MockRepositoryFactory.Create(new List<WaterLog>()).Object);
        _unitOfWork.Setup(u => u.Repository<Achievement>()).Returns(MockRepositoryFactory.Create(new List<Achievement>()).Object);
        _unitOfWork.Setup(u => u.Repository<UserAchievement>()).Returns(MockRepositoryFactory.Create(new List<UserAchievement>()).Object);
        _unitOfWork.Setup(u => u.Repository<MiniGameSession>()).Returns(MockRepositoryFactory.Create(_miniGames).Object);
    }

    private MerfitPointsCalculator CreateCalculator() => new(_unitOfWork.Object, Options.Create(new MerfitScoringOptions()));

    [Fact]
    public async Task PointEvents_IncludeAwardedMiniGames_WithinWindow()
    {
        var day = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
        _sessions.Add(new WorkoutSession { Id = 1, UserId = UserA, Status = WorkoutSessionStatus.Completed, CompletedAt = day });
        _miniGames.Add(new MiniGameSession { Id = 1, UserId = UserA, Status = MiniGameSessionStatus.Completed, CompletedAt = day, TierPoints = 5, AwardedPoints = 5 });
        _miniGames.Add(new MiniGameSession { Id = 2, UserId = UserA, Status = MiniGameSessionStatus.Completed, CompletedAt = day.AddDays(10), TierPoints = 2, AwardedPoints = 2 });
        _miniGames.Add(new MiniGameSession { Id = 3, UserId = UserA, Status = MiniGameSessionStatus.Rejected, CompletedAt = day, TierPoints = 5, AwardedPoints = 0 });
        _miniGames.Add(new MiniGameSession { Id = 4, UserId = UserA, Status = MiniGameSessionStatus.Completed, CompletedAt = day, TierPoints = 0, AwardedPoints = 0 });
        _miniGames.Add(new MiniGameSession { Id = 5, UserId = UserB, Status = MiniGameSessionStatus.Completed, CompletedAt = day, TierPoints = 3, AwardedPoints = 3 });

        var events = await CreateCalculator().GetPointEventsAsync(day.AddDays(-1), day.AddDays(1), UserA);

        Assert.Equal(100, events.Where(e => e.Category == MerfitPointsCalculator.WorkoutsCategory).Sum(e => e.Points));
        var miniGame = Assert.Single(events, e => e.Category == MerfitPointsCalculator.MiniGameCategory);
        Assert.Equal(5, miniGame.Points);
        Assert.Equal(day, miniGame.OccurredAtUtc);
    }
}
