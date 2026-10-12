using MerfitApi.Business.Common;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MerfitApi.Business.Services.Admin;

public record MerfitPointEvent(long UserId, DateTime OccurredAtUtc, string Category, int Points);

/// <summary>
/// MB FIT puan kurallari. MerfitCustomerApi'deki MerfitScoreService.GetPointEventsAsync ile BIREBIR
/// ayni kurallari uygular (ayni veritabani, ayni "MerfitScoring" ayarlari); admin panelindeki puan ve
/// siralama yeniden hesaplamalari mobil uygulamada gorunen puanlarla tutarli kalsin diye kullanilir.
/// Kurallar degisirse iki repoda birlikte guncellenmelidir.
/// </summary>
public class MerfitPointsCalculator
{
    public const string AllTimePeriodTag = "AllTime";

    public const string WorkoutsCategory = "workouts";
    public const string PersonalRecordsCategory = "personal_records";
    public const string NutritionCategory = "nutrition";
    public const string WaterCategory = "water";
    public const string AchievementsCategory = "achievements";

    private readonly IUnitOfWork _unitOfWork;
    private readonly MerfitScoringOptions _options;

    public MerfitPointsCalculator(IUnitOfWork unitOfWork, IOptions<MerfitScoringOptions> options)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    private TimeSpan Offset => TimeSpan.FromHours(_options.TimeZoneOffsetHours);

    public DateTime GetLocalDayStartUtc(DateTime utc) => DateTime.SpecifyKind((utc + Offset).Date - Offset, DateTimeKind.Utc);

    /// <summary>[fromUtc, toUtc) araligindaki puan olaylari; null sinir = sinirsiz. userId verilirse yalnizca o kullanici.</summary>
    public async Task<IReadOnlyList<MerfitPointEvent>> GetPointEventsAsync(DateTime? fromUtc, DateTime? toUtc, long? userId = null, CancellationToken cancellationToken = default)
    {
        var events = new List<MerfitPointEvent>();

        // Antrenmanlar: gunluk ust sinir yerel gune gore uygulanir.
        var sessionQuery = _unitOfWork.Repository<WorkoutSession>().GetQueryable()
            .Where(s => s.Status == WorkoutSessionStatus.Completed && s.CompletedAt != null);
        if (userId.HasValue) sessionQuery = sessionQuery.Where(s => s.UserId == userId.Value);
        if (fromUtc.HasValue) sessionQuery = sessionQuery.Where(s => s.CompletedAt >= fromUtc.Value);
        if (toUtc.HasValue) sessionQuery = sessionQuery.Where(s => s.CompletedAt < toUtc.Value);
        var sessions = await sessionQuery
            .Select(s => new { s.UserId, CompletedAt = s.CompletedAt!.Value })
            .ToListAsync(cancellationToken);

        events.AddRange(sessions
            .GroupBy(s => new { s.UserId, Day = LocalDate(s.CompletedAt) })
            .SelectMany(g => g.OrderBy(s => s.CompletedAt).Take(Math.Max(0, _options.MaxScoredWorkoutsPerDay)))
            .Select(s => new MerfitPointEvent(s.UserId, s.CompletedAt, WorkoutsCategory, _options.WorkoutCompletedPoints)));

        // Kisisel rekorlar.
        var recordQuery = _unitOfWork.Repository<PersonalRecord>().GetQueryable();
        if (userId.HasValue) recordQuery = recordQuery.Where(r => r.UserId == userId.Value);
        if (fromUtc.HasValue) recordQuery = recordQuery.Where(r => r.AchievedAt >= fromUtc.Value);
        if (toUtc.HasValue) recordQuery = recordQuery.Where(r => r.AchievedAt < toUtc.Value);
        var records = await recordQuery.Select(r => new { r.UserId, r.AchievedAt }).ToListAsync(cancellationToken);
        events.AddRange(records.Select(r => new MerfitPointEvent(r.UserId, r.AchievedAt, PersonalRecordsCategory, _options.PersonalRecordPoints)));

        // Ogun kaydi: gunde bir kez.
        var mealQuery =
            from item in _unitOfWork.Repository<MealItem>().GetQueryable()
            join meal in _unitOfWork.Repository<Meal>().GetQueryable() on item.MealId equals meal.Id
            select new { meal.UserId, item.CreatedAt };
        if (userId.HasValue) mealQuery = mealQuery.Where(m => m.UserId == userId.Value);
        if (fromUtc.HasValue) mealQuery = mealQuery.Where(m => m.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue) mealQuery = mealQuery.Where(m => m.CreatedAt < toUtc.Value);
        var mealLogs = await mealQuery.ToListAsync(cancellationToken);
        events.AddRange(FirstPerDay(mealLogs.Select(m => (m.UserId, m.CreatedAt)), NutritionCategory, _options.MealLogDayPoints));

        // Su kaydi: gunde bir kez.
        var waterQuery = _unitOfWork.Repository<WaterLog>().GetQueryable();
        if (userId.HasValue) waterQuery = waterQuery.Where(w => w.UserId == userId.Value);
        if (fromUtc.HasValue) waterQuery = waterQuery.Where(w => w.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue) waterQuery = waterQuery.Where(w => w.CreatedAt < toUtc.Value);
        var waterLogs = await waterQuery.Select(w => new { w.UserId, w.CreatedAt }).ToListAsync(cancellationToken);
        events.AddRange(FirstPerDay(waterLogs.Select(w => (w.UserId, w.CreatedAt)), WaterCategory, _options.WaterLogDayPoints));

        // Basarilar.
        var achievementQuery =
            from ua in _unitOfWork.Repository<UserAchievement>().GetQueryable()
            join a in _unitOfWork.Repository<Achievement>().GetQueryable() on ua.AchievementId equals a.Id
            where a.Points > 0
            select new { ua.UserId, ua.EarnedAt, a.Points };
        if (userId.HasValue) achievementQuery = achievementQuery.Where(x => x.UserId == userId.Value);
        if (fromUtc.HasValue) achievementQuery = achievementQuery.Where(x => x.EarnedAt >= fromUtc.Value);
        if (toUtc.HasValue) achievementQuery = achievementQuery.Where(x => x.EarnedAt < toUtc.Value);
        var achievements = await achievementQuery.ToListAsync(cancellationToken);
        events.AddRange(achievements.Select(x => new MerfitPointEvent(x.UserId, x.EarnedAt, AchievementsCategory, x.Points)));

        return events.Where(e => e.Points > 0).ToList();
    }

    private DateTime LocalDate(DateTime utc) => (utc + Offset).Date;

    private IEnumerable<MerfitPointEvent> FirstPerDay(IEnumerable<(long UserId, DateTime At)> logs, string category, int points) =>
        logs
            .GroupBy(l => new { l.UserId, Day = LocalDate(l.At) })
            .Select(g => new MerfitPointEvent(g.Key.UserId, g.Min(l => l.At), category, points));
}
