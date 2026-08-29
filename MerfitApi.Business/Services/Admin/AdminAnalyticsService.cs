using MerfitApi.Business.Dtos.Admin.Analytics;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>
/// IAdminAnalyticsService'in varsayilan implementasyonu.
///
/// VARSAYIMLAR (madde 33): Repository'de ozel bir "kullanici aktivitesi" olay/analitik tablosu
/// bulunmadigindan, "aktiflik" sinyali olarak WorkoutSession.StartedAt (fitness uygulamasinda
/// en dogrudan katilim gostergesi) kullanilir. DAU/WAU/MAU, retention ve churn tanimlari asagida
/// ilgili metotlarin yaninda yorum olarak belgelenmistir; gercek urun ihtiyaci farklilasirsa
/// bu tanimlar (disaridan sozlesme degismeden) kolayca guncellenebilir.
///
/// Ayni scoped DbContext ornegi thread-safe olmadigindan tum sorgular sirali (sequential)
/// calistirilir (bkz. AdminDashboardService'teki ayni not).
/// </summary>
public class AdminAnalyticsService : IAdminAnalyticsService
{
    private readonly IUnitOfWork _unitOfWork;

    public AdminAnalyticsService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AdminAnalyticsUsersDto> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable().Where(u => u.DeletedAt == null);
        var sessions = _unitOfWork.Repository<WorkoutSession>().GetQueryable();

        var totalUsers = await users.LongCountAsync(cancellationToken);
        var activeUsers = await users.LongCountAsync(u => u.IsActive, cancellationToken);
        var newUsers7 = await users.LongCountAsync(u => u.CreatedAt >= now.AddDays(-7), cancellationToken);
        var newUsers30 = await users.LongCountAsync(u => u.CreatedAt >= now.AddDays(-30), cancellationToken);

        var dau = await ActiveUserCountAsync(sessions, now.AddDays(-1), cancellationToken);
        var wau = await ActiveUserCountAsync(sessions, now.AddDays(-7), cancellationToken);
        var mau = await ActiveUserCountAsync(sessions, now.AddDays(-30), cancellationToken);

        return new AdminAnalyticsUsersDto
        {
            TotalUsers = totalUsers,
            ActiveUsers = activeUsers,
            NewUsersLast7Days = newUsers7,
            NewUsersLast30Days = newUsers30,
            Dau = dau,
            Wau = wau,
            Mau = mau,
        };
    }

    public async Task<AdminAnalyticsWorkoutsDto> GetWorkoutsAsync(CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var sessions = _unitOfWork.Repository<WorkoutSession>().GetQueryable().Where(s => s.StartedAt >= since);
        var workouts = _unitOfWork.Repository<Workout>().GetQueryable();

        var total = await sessions.LongCountAsync(cancellationToken);
        var completed = await sessions.LongCountAsync(s => s.CompletedAt != null, cancellationToken);

        var activeUserCount = await sessions.Select(s => s.UserId).Distinct().CountAsync(cancellationToken);

        var completionRate = total == 0 ? 0 : Math.Round(completed * 100.0 / total, 2);
        var avgPerUser = activeUserCount == 0 ? 0 : Math.Round((double)completed / activeUserCount, 2);

        var topWorkoutsRaw = await sessions
            .GroupBy(s => s.WorkoutId)
            .Select(g => new { WorkoutId = g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync(cancellationToken);

        var workoutIds = topWorkoutsRaw.Select(x => x.WorkoutId).ToList();
        var workoutTitles = await workouts.Where(w => workoutIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Title }).ToListAsync(cancellationToken);

        return new AdminAnalyticsWorkoutsDto
        {
            TotalSessionsLast30Days = total,
            CompletedSessionsLast30Days = completed,
            WorkoutCompletionRatePercent = completionRate,
            AverageWorkoutsPerActiveUser = avgPerUser,
            TopWorkouts = topWorkoutsRaw.Select(x => new AdminAnalyticsTopItemDto
            {
                Id = x.WorkoutId,
                Name = workoutTitles.FirstOrDefault(w => w.Id == x.WorkoutId)?.Title ?? "-",
                Count = x.Count,
            }).ToList(),
        };
    }

    public async Task<AdminAnalyticsNutritionDto> GetNutritionAsync(CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var meals = _unitOfWork.Repository<Meal>().GetQueryable().Where(m => m.Date >= since);
        var mealItems = _unitOfWork.Repository<MealItem>().GetQueryable();

        var totalMeals = await meals.LongCountAsync(cancellationToken);
        var distinctUsers = await meals.Select(m => m.UserId).Distinct().CountAsync(cancellationToken);
        var avgMealsPerUser = distinctUsers == 0 ? 0 : Math.Round((double)totalMeals / distinctUsers, 2);

        var mealIds = await meals.Select(m => m.Id).ToListAsync(cancellationToken);
        double avgCaloriesPerMeal = 0;
        if (mealIds.Count > 0)
        {
            var totalCalories = await mealItems.Where(mi => mealIds.Contains(mi.MealId))
                .SumAsync(mi => (decimal?)mi.Calories, cancellationToken) ?? 0m;
            avgCaloriesPerMeal = Math.Round((double)(totalCalories / mealIds.Count), 2);
        }

        var usersWithGoal = await _unitOfWork.Repository<NutritionGoal>().GetQueryable().LongCountAsync(cancellationToken);

        return new AdminAnalyticsNutritionDto
        {
            TotalMealsLast30Days = totalMeals,
            AverageMealsPerUserLast30Days = avgMealsPerUser,
            AverageCaloriesPerMeal = avgCaloriesPerMeal,
            UsersWithNutritionGoal = usersWithGoal,
        };
    }

    public async Task<AdminAnalyticsSubscriptionsDto> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var subscriptions = _unitOfWork.Repository<Subscription>().GetQueryable();
        var totalUsers = await _unitOfWork.Repository<ApplicationUser>().GetQueryable().LongCountAsync(u => u.DeletedAt == null, cancellationToken);

        var totalSubscriptions = await subscriptions.LongCountAsync(cancellationToken);
        var activeSubscriptions = await subscriptions.LongCountAsync(
            s => s.Status == SubscriptionStatus.Active && (s.ExpiresAt == null || s.ExpiresAt > now), cancellationToken);

        var cancelledLast30Days = await subscriptions.LongCountAsync(
            s => s.CancelledAt != null && s.CancelledAt >= now.AddDays(-30), cancellationToken);

        var conversionRate = totalUsers == 0 ? 0 : Math.Round(activeSubscriptions * 100.0 / totalUsers, 2);

        var churnDenominator = activeSubscriptions + cancelledLast30Days;
        var churnRate = churnDenominator == 0 ? 0 : Math.Round(cancelledLast30Days * 100.0 / churnDenominator, 2);

        return new AdminAnalyticsSubscriptionsDto
        {
            TotalSubscriptions = totalSubscriptions,
            ActiveSubscriptions = activeSubscriptions,
            PlusConversionRatePercent = conversionRate,
            ChurnRatePercent = churnRate,
        };
    }

    public async Task<AdminAnalyticsRevenueDto> GetRevenueAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var transactions = _unitOfWork.Repository<SubscriptionTransaction>().GetQueryable();

        var totalRevenue = await transactions.SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;
        var revenue7 = await transactions.Where(t => t.PurchasedAt >= now.AddDays(-7)).SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;
        var revenue30 = await transactions.Where(t => t.PurchasedAt >= now.AddDays(-30)).SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var payingUserCount = await _unitOfWork.Repository<Subscription>().GetQueryable()
            .Select(s => s.UserId).Distinct().CountAsync(cancellationToken);

        var avgRevenuePerPayingUser = payingUserCount == 0 ? 0 : Math.Round(totalRevenue / payingUserCount, 2);

        return new AdminAnalyticsRevenueDto
        {
            TotalRevenue = totalRevenue,
            RevenueLast7Days = revenue7,
            RevenueLast30Days = revenue30,
            AverageRevenuePerPayingUser = avgRevenuePerPayingUser,
        };
    }

    public async Task<AdminAnalyticsRetentionDto> GetRetentionAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable().Where(u => u.DeletedAt == null);
        var sessions = _unitOfWork.Repository<WorkoutSession>().GetQueryable();

        var retention7 = await CalculateRetentionAsync(users, sessions, now, days: 7, cancellationToken);
        var retention30 = await CalculateRetentionAsync(users, sessions, now, days: 30, cancellationToken);

        return new AdminAnalyticsRetentionDto
        {
            Retention7DayPercent = retention7,
            Retention30DayPercent = retention30,
        };
    }

    public async Task<AdminAnalyticsEngagementDto> GetEngagementAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var sessions = _unitOfWork.Repository<WorkoutSession>().GetQueryable();

        var dau = await ActiveUserCountAsync(sessions, now.AddDays(-1), cancellationToken);
        var wau = await ActiveUserCountAsync(sessions, now.AddDays(-7), cancellationToken);
        var mau = await ActiveUserCountAsync(sessions, now.AddDays(-30), cancellationToken);

        var stickiness = mau == 0 ? 0 : Math.Round(dau * 100.0 / mau, 2);

        var last30 = sessions.Where(s => s.StartedAt >= now.AddDays(-30));
        var last30Count = await last30.LongCountAsync(cancellationToken);
        var last30Users = await last30.Select(s => s.UserId).Distinct().CountAsync(cancellationToken);
        var avgSessionsPerActiveUser = last30Users == 0 ? 0 : Math.Round((double)last30Count / last30Users, 2);

        return new AdminAnalyticsEngagementDto
        {
            Dau = dau,
            Wau = wau,
            Mau = mau,
            StickinessPercent = stickiness,
            AverageSessionsPerActiveUserLast30Days = avgSessionsPerActiveUser,
        };
    }

    /// <summary>Verilen tarihten sonra en az bir WorkoutSession baslatmis benzersiz kullanici sayisini doner.</summary>
    private static async Task<long> ActiveUserCountAsync(IQueryable<WorkoutSession> sessions, DateTime since, CancellationToken cancellationToken)
    {
        return await sessions.Where(s => s.StartedAt >= since).Select(s => s.UserId).Distinct().LongCountAsync(cancellationToken);
    }

    /// <summary>
    /// Basitlestirilmis "Day-N retention": kayittan en az N gun once katilmis kullanicilardan,
    /// kayit tarihi ile kayit tarihi+N gun arasinda en az bir antrenman TAMAMLAMIS olanlarin orani.
    /// </summary>
    private static async Task<double> CalculateRetentionAsync(
        IQueryable<ApplicationUser> users, IQueryable<WorkoutSession> sessions, DateTime now, int days, CancellationToken cancellationToken)
    {
        var cutoff = now.AddDays(-days);
        var eligibleUsers = users.Where(u => u.CreatedAt <= cutoff);

        var eligibleCount = await eligibleUsers.LongCountAsync(cancellationToken);
        if (eligibleCount == 0)
        {
            return 0;
        }

        var retainedCount = await eligibleUsers.LongCountAsync(
            u => sessions.Any(s => s.UserId == u.Id && s.CompletedAt != null
                && s.CompletedAt >= u.CreatedAt && s.CompletedAt <= u.CreatedAt.AddDays(days)),
            cancellationToken);

        return Math.Round(retainedCount * 100.0 / eligibleCount, 2);
    }
}
