using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Users;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>
/// IAdminUserService'in varsayilan implementasyonu (madde 6). Kullaniciya ait tum alt kaynaklar
/// (workout, meal, score, device vb.) salt-okunur olarak sunulur; sadece durum/soft-delete
/// islemleri mutasyon icerir ve audit log'a yazilir (madde 32/44).
///
/// NOT (EF Core sinirlamasi): Enum alanlarin ".ToString()" cagrisi bazi provider'larda SQL'e
/// cevrilemeyebildiginden, sorgular once enum degerini oldugu gibi (int/enum) projekte edip
/// veritabanindan cektikten (ToListAsync) SONRA bellek icinde (LINQ to Objects) string'e
/// cevirir. Boylece hem database tarafinda filtreleme/siralama/sayfalama (Skip/Take) korunur
/// hem de "could not be translated" riskine girilmez.
/// </summary>
public class AdminUserService : IAdminUserService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<ApplicationUser, object?>>> UserSortMap =
        new Dictionary<string, Expression<Func<ApplicationUser, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = x => x.CreatedAt,
            ["lastLoginAt"] = x => x.LastLoginAt,
            ["email"] = x => x.Email,
            ["isActive"] = x => x.IsActive,
        };

    public AdminUserService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminUserListItemDto>> GetListAsync(AdminUserListRequest request, CancellationToken cancellationToken = default)
    {
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();
        var profiles = _unitOfWork.Repository<UserProfile>().GetQueryable();
        var subscriptions = _unitOfWork.Repository<Subscription>().GetQueryable();

        if (!request.IncludeDeleted)
        {
            users = users.Where(u => u.DeletedAt == null);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            users = users.Where(u => u.Email.Contains(term) || u.UserName.Contains(term)
                || profiles.Any(p => p.UserId == u.Id && (p.FirstName.Contains(term) || p.LastName.Contains(term))));
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            users = users.Where(u => u.Email.Contains(request.Email));
        }

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            users = users.Where(u => u.UserName.Contains(request.Username));
        }

        if (request.IsActive.HasValue)
        {
            users = users.Where(u => u.IsActive == request.IsActive.Value);
        }

        if (request.EmailConfirmed.HasValue)
        {
            users = users.Where(u => u.EmailConfirmed == request.EmailConfirmed.Value);
        }

        if (request.CreatedFrom.HasValue)
        {
            users = users.Where(u => u.CreatedAt >= request.CreatedFrom.Value);
        }

        if (request.CreatedTo.HasValue)
        {
            users = users.Where(u => u.CreatedAt <= request.CreatedTo.Value);
        }

        if (request.LastLoginFrom.HasValue)
        {
            users = users.Where(u => u.LastLoginAt != null && u.LastLoginAt >= request.LastLoginFrom.Value);
        }

        if (request.LastLoginTo.HasValue)
        {
            users = users.Where(u => u.LastLoginAt != null && u.LastLoginAt <= request.LastLoginTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SubscriptionStatus))
        {
            if (string.Equals(request.SubscriptionStatus, "none", StringComparison.OrdinalIgnoreCase))
            {
                users = users.Where(u => !subscriptions.Any(s => s.UserId == u.Id));
            }
            else if (Enum.TryParse<SubscriptionStatus>(request.SubscriptionStatus, true, out var status))
            {
                users = users.Where(u => subscriptions.Any(s => s.UserId == u.Id && s.Status == status));
            }
        }

        var totalCount = await users.LongCountAsync(cancellationToken);

        var ordered = users.ApplySort(request.SortBy, request.IsDescending, UserSortMap, defaultKey: "createdAt");

        var page = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.UserName,
                u.Role,
                u.IsActive,
                u.EmailConfirmed,
                u.DeletedAt,
                u.CreatedAt,
                u.LastLoginAt,
                FirstName = profiles.Where(p => p.UserId == u.Id).Select(p => p.FirstName).FirstOrDefault(),
                LastName = profiles.Where(p => p.UserId == u.Id).Select(p => p.LastName).FirstOrDefault(),
                LatestSubscriptionStatus = subscriptions
                    .Where(s => s.UserId == u.Id)
                    .OrderByDescending(s => s.StartedAt)
                    .Select(s => (SubscriptionStatus?)s.Status)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = page.Select(x => new AdminUserListItemDto
        {
            Id = x.Id,
            Email = x.Email,
            UserName = x.UserName,
            FirstName = x.FirstName,
            LastName = x.LastName,
            Role = x.Role.ToString(),
            IsActive = x.IsActive,
            EmailConfirmed = x.EmailConfirmed,
            IsDeleted = x.DeletedAt != null,
            SubscriptionStatus = x.LatestSubscriptionStatus?.ToString(),
            CreatedAt = x.CreatedAt,
            LastLoginAt = x.LastLoginAt,
        }).ToList();

        return PagedResult<AdminUserListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminUserDetailDto> GetByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        var user = await GetUserOrThrowAsync(userId, cancellationToken);
        var profile = await _unitOfWork.Repository<UserProfile>().GetQueryable()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        var now = DateTime.UtcNow;
        var currentSubscription = await _unitOfWork.Repository<Subscription>().GetQueryable()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new { s.Status, s.SubscriptionProductId, s.ExpiresAt })
            .FirstOrDefaultAsync(cancellationToken);

        string? productName = null;
        if (currentSubscription is not null)
        {
            productName = await _unitOfWork.Repository<SubscriptionProduct>().GetQueryable()
                .Where(p => p.Id == currentSubscription.SubscriptionProductId)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var latestScore = await _unitOfWork.Repository<MerfitScore>().GetQueryable()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CalculatedAt)
            .Select(s => (decimal?)s.Score)
            .FirstOrDefaultAsync(cancellationToken);

        var streak = await _unitOfWork.Repository<UserStreak>().GetQueryable()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        var totalWorkoutSessions = await _unitOfWork.Repository<WorkoutSession>().GetQueryable()
            .LongCountAsync(s => s.UserId == userId, cancellationToken);

        var totalAchievements = await _unitOfWork.Repository<UserAchievement>().GetQueryable()
            .LongCountAsync(a => a.UserId == userId, cancellationToken);

        return new AdminUserDetailDto
        {
            Id = user.Id,
            Email = user.Email,
            UserName = user.UserName,
            PhoneNumber = user.PhoneNumber,
            Role = user.Role.ToString(),
            IsActive = user.IsActive,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            IsDeleted = user.DeletedAt != null,
            DeletedAt = user.DeletedAt,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            Profile = profile is null ? null : MapProfile(profile),
            CurrentSubscriptionStatus = currentSubscription?.Status.ToString(),
            CurrentSubscriptionProductName = productName,
            CurrentSubscriptionExpiresAt = currentSubscription?.ExpiresAt,
            LatestMerfitScore = latestScore,
            CurrentStreak = streak?.CurrentStreak ?? 0,
            LongestStreak = streak?.LongestStreak ?? 0,
            TotalWorkoutSessions = totalWorkoutSessions,
            TotalAchievements = totalAchievements,
        };
    }

    public async Task UpdateStatusAsync(long userId, AdminUpdateUserStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<ApplicationUser>();
        var user = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        var oldValue = new { user.IsActive };

        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedUser = adminUserId;

        repo.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(
            adminUserId, "USER_STATUS_CHANGED", nameof(ApplicationUser), userId, ipAddress, userAgent,
            oldValue, new { user.IsActive, request.Reason }, cancellationToken);
    }

    public async Task SoftDeleteAsync(long userId, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<ApplicationUser>();
        var user = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        if (user.DeletedAt != null)
        {
            throw new ConflictException("Kullanici zaten silinmis durumda.");
        }

        user.DeletedAt = DateTime.UtcNow;
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedUser = adminUserId;

        repo.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "USER_DELETED", nameof(ApplicationUser), userId, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    public async Task RestoreAsync(long userId, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<ApplicationUser>();
        var user = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        if (user.DeletedAt == null)
        {
            throw new ConflictException("Kullanici zaten aktif (silinmemis) durumda.");
        }

        user.DeletedAt = null;
        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedUser = adminUserId;

        repo.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "USER_RESTORED", nameof(ApplicationUser), userId, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    public async Task<AdminUserProfileDto> GetProfileAsync(long userId, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var profile = await _unitOfWork.Repository<UserProfile>().GetQueryable()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        return profile is null
            ? new AdminUserProfileDto()
            : MapProfile(profile);
    }

    public async Task<PagedResult<AdminUserSubscriptionListItemDto>> GetSubscriptionsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var query = _unitOfWork.Repository<Subscription>().GetQueryable()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.StartedAt);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var raw = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var productIds = raw.Select(s => s.SubscriptionProductId).Distinct().ToList();
        var products = await _unitOfWork.Repository<SubscriptionProduct>().GetQueryable()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);

        var items = raw.Select(s => new AdminUserSubscriptionListItemDto
        {
            Id = s.Id,
            SubscriptionProductId = s.SubscriptionProductId,
            ProductName = products.FirstOrDefault(p => p.Id == s.SubscriptionProductId)?.Name ?? "-",
            Provider = s.Provider.ToString(),
            Status = s.Status.ToString(),
            StartedAt = s.StartedAt,
            ExpiresAt = s.ExpiresAt,
            AutoRenew = s.AutoRenew,
            CancelledAt = s.CancelledAt,
        }).ToList();

        return PagedResult<AdminUserSubscriptionListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<PagedResult<AdminUserWorkoutSessionListItemDto>> GetWorkoutSessionsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var query = _unitOfWork.Repository<WorkoutSession>().GetQueryable()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.StartedAt);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var raw = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var workoutIds = raw.Select(s => s.WorkoutId).Distinct().ToList();
        var workouts = await _unitOfWork.Repository<Workout>().GetQueryable()
            .Where(w => workoutIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Title })
            .ToListAsync(cancellationToken);

        var items = raw.Select(s => new AdminUserWorkoutSessionListItemDto
        {
            Id = s.Id,
            WorkoutId = s.WorkoutId,
            WorkoutTitle = workouts.FirstOrDefault(w => w.Id == s.WorkoutId)?.Title ?? "-",
            StartedAt = s.StartedAt,
            CompletedAt = s.CompletedAt,
            DurationSeconds = s.DurationSeconds,
            CaloriesBurned = s.CaloriesBurned,
        }).ToList();

        return PagedResult<AdminUserWorkoutSessionListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<PagedResult<AdminUserWorkoutPlanListItemDto>> GetWorkoutPlansAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var query = _unitOfWork.Repository<WorkoutPlan>().GetQueryable()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.StartDate);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var raw = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = raw.Select(p => new AdminUserWorkoutPlanListItemDto
        {
            Id = p.Id,
            Name = p.Name,
            Goal = p.Goal,
            StartDate = p.StartDate,
            EndDate = p.EndDate,
            IsActive = p.IsActive,
            IsAiGenerated = p.IsAiGenerated,
        }).ToList();

        return PagedResult<AdminUserWorkoutPlanListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<PagedResult<AdminUserMealListItemDto>> GetMealsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var meals = _unitOfWork.Repository<Meal>().GetQueryable();
        var mealItems = _unitOfWork.Repository<MealItem>().GetQueryable();

        var query = meals.Where(m => m.UserId == userId).OrderByDescending(m => m.Date);

        var totalCount = await query.LongCountAsync(cancellationToken);

        var page = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m => new AdminUserMealListItemDto
            {
                Id = m.Id,
                Date = m.Date,
                Notes = m.Notes,
                ItemCount = mealItems.Count(mi => mi.MealId == m.Id),
                TotalCalories = mealItems.Where(mi => mi.MealId == m.Id).Sum(mi => (decimal?)mi.Calories) ?? 0m,
            })
            .ToListAsync(cancellationToken);

        return PagedResult<AdminUserMealListItemDto>.Create(page, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminUserNutritionGoalDto?> GetNutritionGoalAsync(long userId, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        return await _unitOfWork.Repository<NutritionGoal>().GetQueryable()
            .Where(g => g.UserId == userId)
            .Select(g => new AdminUserNutritionGoalDto
            {
                DailyCalories = g.DailyCalories,
                ProteinTarget = g.ProteinTarget,
                CarbsTarget = g.CarbsTarget,
                FatTarget = g.FatTarget,
                WaterTargetMl = g.WaterTargetMl,
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PagedResult<AdminUserWaterLogListItemDto>> GetWaterLogsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var query = _unitOfWork.Repository<WaterLog>().GetQueryable()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.Date)
            .Select(w => new AdminUserWaterLogListItemDto { Id = w.Id, Date = w.Date, AmountMl = w.AmountMl });

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<PagedResult<AdminUserMeasurementListItemDto>> GetMeasurementsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var query = _unitOfWork.Repository<BodyMeasurement>().GetQueryable()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.RecordedAt)
            .Select(m => new AdminUserMeasurementListItemDto
            {
                Id = m.Id,
                RecordedAt = m.RecordedAt,
                WeightKg = m.WeightKg,
                BodyFatPercentage = m.BodyFatPercentage,
                BMI = m.BMI,
                ChestCm = m.ChestCm,
                WaistCm = m.WaistCm,
                HipCm = m.HipCm,
                ArmCm = m.ArmCm,
                ThighCm = m.ThighCm,
            });

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<AdminUserScoreDto> GetScoreAsync(long userId, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var latest = await _unitOfWork.Repository<MerfitScore>().GetQueryable()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return latest is null
            ? new AdminUserScoreDto { Score = 0 }
            : new AdminUserScoreDto { Id = latest.Id, Score = latest.Score, Period = latest.Period, CalculatedAt = latest.CalculatedAt };
    }

    public async Task<PagedResult<AdminUserScoreHistoryListItemDto>> GetScoreHistoryAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var query = _unitOfWork.Repository<MerfitScoreHistory>().GetQueryable()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.RecordedAt)
            .Select(h => new AdminUserScoreHistoryListItemDto { Id = h.Id, Score = h.Score, RecordedAt = h.RecordedAt });

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<List<AdminUserScoreBreakdownItemDto>> GetScoreBreakdownAsync(long userId, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var latestScoreId = await _unitOfWork.Repository<MerfitScore>().GetQueryable()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CalculatedAt)
            .Select(s => (long?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestScoreId is null)
        {
            return new List<AdminUserScoreBreakdownItemDto>();
        }

        return await _unitOfWork.Repository<MerfitScoreBreakdown>().GetQueryable()
            .Where(b => b.MerfitScoreId == latestScoreId.Value)
            .Select(b => new AdminUserScoreBreakdownItemDto { Category = b.Category, Points = b.Points })
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<AdminUserAchievementListItemDto>> GetAchievementsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var userAchievements = _unitOfWork.Repository<UserAchievement>().GetQueryable();
        var achievements = _unitOfWork.Repository<Achievement>().GetQueryable();

        var query = from ua in userAchievements
                     where ua.UserId == userId
                     join a in achievements on ua.AchievementId equals a.Id
                     orderby ua.EarnedAt descending
                     select new AdminUserAchievementListItemDto
                     {
                         AchievementId = a.Id,
                         Code = a.Code,
                         Title = a.Title,
                         Points = a.Points,
                         EarnedAt = ua.EarnedAt,
                     };

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<PagedResult<AdminUserDeviceListItemDto>> GetDevicesAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var query = _unitOfWork.Repository<UserDevice>().GetQueryable()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAt)
            .Select(d => new AdminUserDeviceListItemDto
            {
                Id = d.Id,
                DeviceToken = d.DeviceToken,
                Platform = d.Platform,
                DeviceName = d.DeviceName,
                AppVersion = d.AppVersion,
                OsVersion = d.OsVersion,
                IsActive = d.IsActive,
                LastSeenAt = d.LastSeenAt,
            });

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<PagedResult<AdminUserConsentListItemDto>> GetConsentsAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(userId, cancellationToken);

        var consents = _unitOfWork.Repository<UserConsent>().GetQueryable();
        var documents = _unitOfWork.Repository<LegalDocument>().GetQueryable();

        var query = from c in consents
                     where c.UserId == userId
                     join d in documents on c.DocumentId equals d.Id into dj
                     from d in dj.DefaultIfEmpty()
                     orderby c.AcceptedAt descending
                     select new AdminUserConsentListItemDto
                     {
                         Id = c.Id,
                         DocumentId = c.DocumentId,
                         DocumentTitle = d != null ? d.Title : "-",
                         Accepted = c.Accepted,
                         AcceptedAt = c.AcceptedAt,
                         Version = c.Version,
                         IpAddress = c.IpAddress,
                     };

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    private async Task<ApplicationUser> GetUserOrThrowAsync(long userId, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<ApplicationUser>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);
    }

    private static AdminUserProfileDto MapProfile(UserProfile profile) => new()
    {
        FirstName = profile.FirstName,
        LastName = profile.LastName,
        Username = profile.Username,
        DateOfBirth = profile.DateOfBirth,
        Gender = profile.Gender,
        HeightCm = profile.HeightCm,
        WeightKg = profile.WeightKg,
        TargetWeightKg = profile.TargetWeightKg,
        ProfileImageUrl = profile.ProfileImageUrl,
        Goal = profile.Goal,
        ExperienceLevel = profile.ExperienceLevel,
        ActivityLevel = profile.ActivityLevel,
        TrainingLocation = profile.TrainingLocation,
        TrainingDaysPerWeek = profile.TrainingDaysPerWeek,
        UnitSystem = profile.UnitSystem,
    };
}
