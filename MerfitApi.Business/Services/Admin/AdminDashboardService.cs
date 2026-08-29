using MerfitApi.Business.Dtos.Admin.Dashboard;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>
/// IAdminDashboardService'in varsayilan implementasyonu.
/// Tum metrikler database tarafinda (COUNT/SUM/GROUP BY) hesaplanir; hicbir sorguda tum
/// tablo memory'e cekilmez (bkz. madde 7/40 - performans kurallari).
/// NOT: Ayni scoped AppDbContext ornegi thread-safe olmadigindan (paralel sorgu calistirmak
/// InvalidOperationException'a yol acar), metrikler Task.WhenAll yerine sirali (sequential)
/// olarak calistirilir; her biri tek basina hafif birer indeksli COUNT/SUM sorgusudur.
/// </summary>
public class AdminDashboardService : IAdminDashboardService
{
    private readonly IUnitOfWork _unitOfWork;

    public AdminDashboardService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AdminDashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();
        var subscriptions = _unitOfWork.Repository<Subscription>().GetQueryable();
        var transactions = _unitOfWork.Repository<SubscriptionTransaction>().GetQueryable();
        var sessions = _unitOfWork.Repository<WorkoutSession>().GetQueryable();
        var tickets = _unitOfWork.Repository<SupportTicket>().GetQueryable();
        var aiRequests = _unitOfWork.Repository<AiGenerationRequest>().GetQueryable();
        var workouts = _unitOfWork.Repository<Workout>().GetQueryable();
        var exercises = _unitOfWork.Repository<Exercise>().GetQueryable();

        var totalUsers = await users.LongCountAsync(u => u.DeletedAt == null, cancellationToken);
        var activeUsers = await users.LongCountAsync(u => u.DeletedAt == null && u.IsActive, cancellationToken);
        var newUsersToday = await users.LongCountAsync(u => u.DeletedAt == null && u.CreatedAt >= todayStart, cancellationToken);
        var newUsersThisMonth = await users.LongCountAsync(u => u.DeletedAt == null && u.CreatedAt >= monthStart, cancellationToken);

        var plusSubscribers = await subscriptions
            .Where(IsActiveSubscription(now))
            .Select(s => s.UserId)
            .Distinct()
            .LongCountAsync(cancellationToken);

        var activeSubscriptions = await subscriptions.LongCountAsync(IsActiveSubscription(now), cancellationToken);

        var revenueThisMonth = await transactions
            .Where(t => t.PurchasedAt >= monthStart)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var revenueTotal = await transactions.SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var workoutsCompleted = await sessions.LongCountAsync(s => s.CompletedAt != null, cancellationToken);

        var openSupportTickets = await tickets.LongCountAsync(
            t => t.Status != Domain.Entities.Enums.SupportTicketStatus.Closed
                && t.Status != Domain.Entities.Enums.SupportTicketStatus.Resolved,
            cancellationToken);

        var aiRequestCount = await aiRequests.LongCountAsync(cancellationToken);

        var activeWorkouts = await workouts.LongCountAsync(w => w.IsActive, cancellationToken);
        var activeExercises = await exercises.LongCountAsync(e => e.IsActive, cancellationToken);

        return new AdminDashboardSummaryDto
        {
            TotalUsers = totalUsers,
            ActiveUsers = activeUsers,
            NewUsersToday = newUsersToday,
            NewUsersThisMonth = newUsersThisMonth,
            PlusSubscribers = plusSubscribers,
            ActiveSubscriptions = activeSubscriptions,
            RevenueThisMonth = revenueThisMonth,
            RevenueTotal = revenueTotal,
            WorkoutsCompleted = workoutsCompleted,
            OpenSupportTickets = openSupportTickets,
            AiRequestCount = aiRequestCount,
            ActiveWorkouts = activeWorkouts,
            ActiveExercises = activeExercises,
        };
    }

    public async Task<AdminUserGrowthDto> GetUserGrowthAsync(AdminDashboardDateRangeRequest request, CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(request, defaultDays: 30);

        var grouped = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.DeletedAt == null && u.CreatedAt >= from && u.CreatedAt <= to)
            .GroupBy(u => u.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.LongCount() })
            .OrderBy(g => g.Date)
            .ToListAsync(cancellationToken);

        return new AdminUserGrowthDto
        {
            From = from,
            To = to,
            Points = grouped
                .Select(g => new AdminDashboardTimeSeriesPointDto { Date = DateOnly.FromDateTime(g.Date), Count = g.Count })
                .ToList(),
        };
    }

    public async Task<AdminRevenueDto> GetRevenueAsync(AdminDashboardDateRangeRequest request, CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(request, defaultDays: 30);

        var grouped = await _unitOfWork.Repository<SubscriptionTransaction>().GetQueryable()
            .Where(t => t.PurchasedAt >= from && t.PurchasedAt <= to)
            .GroupBy(t => t.PurchasedAt.Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(t => t.Amount), Count = g.LongCount() })
            .OrderBy(g => g.Date)
            .ToListAsync(cancellationToken);

        return new AdminRevenueDto
        {
            From = from,
            To = to,
            TotalRevenue = grouped.Sum(g => g.Amount),
            Points = grouped
                .Select(g => new AdminDashboardTimeSeriesPointDto { Date = DateOnly.FromDateTime(g.Date), Amount = g.Amount, Count = g.Count })
                .ToList(),
        };
    }

    public async Task<AdminDashboardSubscriptionsDto> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var subscriptions = _unitOfWork.Repository<Subscription>().GetQueryable();

        var statusCounts = await subscriptions
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.LongCount() })
            .ToListAsync(cancellationToken);

        long CountFor(Domain.Entities.Enums.SubscriptionStatus status) =>
            statusCounts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        var now = DateTime.UtcNow;
        var byProduct = await subscriptions
            .Where(IsActiveSubscription(now))
            .GroupBy(s => s.SubscriptionProductId)
            .Select(g => new { SubscriptionProductId = g.Key, Count = g.LongCount() })
            .ToListAsync(cancellationToken);

        var productIds = byProduct.Select(x => x.SubscriptionProductId).ToList();
        var productNames = await _unitOfWork.Repository<SubscriptionProduct>().GetQueryable()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);

        return new AdminDashboardSubscriptionsDto
        {
            ActiveCount = CountFor(Domain.Entities.Enums.SubscriptionStatus.Active),
            ExpiredCount = CountFor(Domain.Entities.Enums.SubscriptionStatus.Expired),
            CancelledCount = CountFor(Domain.Entities.Enums.SubscriptionStatus.Cancelled),
            GracePeriodCount = CountFor(Domain.Entities.Enums.SubscriptionStatus.GracePeriod),
            ByProduct = byProduct.Select(x => new AdminSubscriptionProductBreakdownDto
            {
                SubscriptionProductId = x.SubscriptionProductId,
                ProductName = productNames.FirstOrDefault(p => p.Id == x.SubscriptionProductId)?.Name ?? "-",
                ActiveCount = x.Count,
            }).ToList(),
        };
    }

    public async Task<AdminDashboardActivityDto> GetActivityAsync(AdminDashboardDateRangeRequest request, CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(request, defaultDays: 30);

        var workoutsCompleted = await _unitOfWork.Repository<WorkoutSession>().GetQueryable()
            .Where(s => s.CompletedAt != null && s.CompletedAt >= from && s.CompletedAt <= to)
            .GroupBy(s => s.CompletedAt!.Value.Date)
            .Select(g => new { Date = g.Key, Count = g.LongCount() })
            .OrderBy(g => g.Date)
            .ToListAsync(cancellationToken);

        var newUsers = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.DeletedAt == null && u.CreatedAt >= from && u.CreatedAt <= to)
            .GroupBy(u => u.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.LongCount() })
            .OrderBy(g => g.Date)
            .ToListAsync(cancellationToken);

        return new AdminDashboardActivityDto
        {
            From = from,
            To = to,
            WorkoutsCompleted = workoutsCompleted
                .Select(g => new AdminDashboardTimeSeriesPointDto { Date = DateOnly.FromDateTime(g.Date), Count = g.Count })
                .ToList(),
            NewUsers = newUsers
                .Select(g => new AdminDashboardTimeSeriesPointDto { Date = DateOnly.FromDateTime(g.Date), Count = g.Count })
                .ToList(),
        };
    }

    /// <summary>Bir aboneligin "aktif" sayilmasi icin gereken kosulu ifade eden expression uretir.</summary>
    private static System.Linq.Expressions.Expression<Func<Subscription, bool>> IsActiveSubscription(DateTime now)
    {
        return s => s.CancelledAt == null
            && s.Status == Domain.Entities.Enums.SubscriptionStatus.Active
            && (s.ExpiresAt == null || s.ExpiresAt > now);
    }

    private static (DateTime From, DateTime To) ResolveRange(AdminDashboardDateRangeRequest request, int defaultDays)
    {
        var to = request.To ?? DateTime.UtcNow;
        var from = request.From ?? to.AddDays(-defaultDays);
        return (from, to);
    }
}
