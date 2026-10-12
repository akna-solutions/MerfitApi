using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.WorkoutPlans;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminWorkoutPlanService'in varsayilan implementasyonu.</summary>
public class AdminWorkoutPlanService : IAdminWorkoutPlanService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<WorkoutPlan, object?>>> SortMap =
        new Dictionary<string, Expression<Func<WorkoutPlan, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["startDate"] = x => x.StartDate,
            ["createdAt"] = x => x.CreatedAt,
            ["name"] = x => x.Name,
        };

    public AdminWorkoutPlanService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminWorkoutPlanListItemDto>> GetListAsync(AdminWorkoutPlanListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<WorkoutPlan>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        if (request.UserId.HasValue) query = query.Where(x => x.UserId == request.UserId.Value);
        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);
        if (request.IsAiGenerated.HasValue) query = query.Where(x => x.IsAiGenerated == request.IsAiGenerated.Value);
        if (request.Goal.HasValue) query = query.Where(x => x.Goal == request.Goal.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Name.Contains(term) || users.Any(u => u.Id == x.UserId && u.Email.Contains(term)));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var ordered = query.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "createdAt");

        var raw = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.Name,
                x.Goal,
                x.StartDate,
                x.EndDate,
                x.IsActive,
                x.IsAiGenerated,
                x.CreatedAt,
                UserEmail = users.Where(u => u.Id == x.UserId).Select(u => u.Email).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = raw.Select(x => new AdminWorkoutPlanListItemDto
        {
            Id = x.Id,
            UserId = x.UserId,
            UserEmail = x.UserEmail ?? "-",
            Name = x.Name,
            Goal = x.Goal,
            StartDate = x.StartDate,
            EndDate = x.EndDate,
            IsActive = x.IsActive,
            IsAiGenerated = x.IsAiGenerated,
            CreatedAt = x.CreatedAt,
        }).ToList();

        return PagedResult<AdminWorkoutPlanListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminWorkoutPlanDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);

        var userEmail = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == entity.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        var dayCount = await _unitOfWork.Repository<WorkoutPlanDay>().GetQueryable()
            .CountAsync(d => d.WorkoutPlanId == id, cancellationToken);

        return new AdminWorkoutPlanDetailDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            UserEmail = userEmail ?? "-",
            Name = entity.Name,
            Goal = entity.Goal,
            StartDate = entity.StartDate,
            EndDate = entity.EndDate,
            IsActive = entity.IsActive,
            IsAiGenerated = entity.IsAiGenerated,
            CreatedAt = entity.CreatedAt,
            Description = entity.Description,
            DayCount = dayCount,
        };
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateWorkoutPlanStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<WorkoutPlan>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutPlan), id);

        var oldValue = new { entity.IsActive };
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_PLAN_STATUS_CHANGED", nameof(WorkoutPlan), id, ipAddress, userAgent, oldValue, new { entity.IsActive }, cancellationToken);
    }

    public async Task<List<AdminWorkoutPlanDayItemDto>> GetDaysAsync(long id, CancellationToken cancellationToken = default)
    {
        await GetOrThrowAsync(id, cancellationToken);

        var days = _unitOfWork.Repository<WorkoutPlanDay>().GetQueryable();
        var workouts = _unitOfWork.Repository<Workout>().GetQueryable();

        var planExercises = _unitOfWork.Repository<WorkoutPlanExercise>().GetQueryable();

        var rows = await (from d in days
                          where d.WorkoutPlanId == id
                          join w in workouts on d.WorkoutId equals w.Id
                          orderby d.Order, ((int)d.DayOfWeek + 6) % 7 // MerfitCustomerApi gunleri Order=0 ile uretir; hafta sirasi esas alinir
                          select new
                          {
                              d.WorkoutId,
                              w.Title,
                              d.Order,
                              d.DayOfWeek,
                              ExerciseCount = planExercises.Count(pe => pe.WorkoutPlanDayId == d.Id),
                          }).ToListAsync(cancellationToken);

        return rows.Select(r => new AdminWorkoutPlanDayItemDto
        {
            WorkoutId = r.WorkoutId,
            WorkoutTitle = r.Title,
            Order = r.Order,
            DayOfWeek = DayCodes[((int)r.DayOfWeek + 6) % 7],
            ExerciseCount = r.ExerciseCount,
        }).ToList();
    }

    public async Task<List<AdminWorkoutPlanDayItemDto>> SetDaysAsync(long id, AdminSetWorkoutPlanDaysRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await GetOrThrowAsync(id, cancellationToken);

        var workoutIds = request.Days.Select(x => x.WorkoutId).Distinct().ToList();
        if (workoutIds.Count > 0)
        {
            var existingCount = await _unitOfWork.Repository<Workout>().GetQueryable()
                .CountAsync(w => workoutIds.Contains(w.Id), cancellationToken);

            if (existingCount != workoutIds.Count)
            {
                throw new AppValidationException(nameof(request.Days), "Listede bulunmayan bir antrenman (workout) Id'si var.");
            }
        }

        var repo = _unitOfWork.Repository<WorkoutPlanDay>();
        var existing = (await repo.FindAsync(x => x.WorkoutPlanId == id, cancellationToken))
            .OrderBy(x => x.Order)
            .ThenBy(x => ((int)x.DayOfWeek + 6) % 7)
            .ToList();
        var orderedRequest = request.Days.OrderBy(x => x.Order).ToList();
        var dayOfWeeks = ResolveDaysOfWeek(orderedRequest, existing);

        // Mobil uygulama (MerfitCustomerApi) plan gununden baslatilan antrenmanda hedef set/tekrar
        // degerlerini WorkoutPlanExercise'dan okur; bu kayitlar olmadan gun bos antrenman acar.
        // Ayni antrenman planda zaten varsa kullaniciya ozel degerleri korunur, yoksa antrenmanin
        // katalogdaki (WorkoutExercise) degerleri kopyalanir.
        var existingDayIds = existing.Select(d => d.Id).ToList();
        var existingPlanExercises = await _unitOfWork.Repository<WorkoutPlanExercise>()
            .FindAsync(x => existingDayIds.Contains(x.WorkoutPlanDayId), cancellationToken);
        var catalogExercises = await _unitOfWork.Repository<WorkoutExercise>()
            .FindAsync(x => workoutIds.Contains(x.WorkoutId), cancellationToken);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // Eski gunlerin WorkoutPlanExercise kayitlari veritabaninda cascade ile, bu gunlere bagli
        // gecmis WorkoutSession.WorkoutPlanDayId degerleri SET NULL ile temizlenir.
        repo.RemoveRange(existing);

        var newDays = orderedRequest.Select((x, index) => new WorkoutPlanDay
        {
            WorkoutPlanId = id,
            WorkoutId = x.WorkoutId,
            Order = x.Order,
            DayOfWeek = dayOfWeeks[index],
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        }).ToList();

        await repo.AddRangeAsync(newDays, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken); // yeni gunlerin Id'leri atanir

        var newPlanExercises = new List<WorkoutPlanExercise>();
        foreach (var day in newDays)
        {
            var previousDay = existing.FirstOrDefault(d => d.WorkoutId == day.WorkoutId);
            var previous = previousDay is null
                ? new List<WorkoutPlanExercise>()
                : existingPlanExercises.Where(pe => pe.WorkoutPlanDayId == previousDay.Id).ToList();

            if (previous.Count > 0)
            {
                newPlanExercises.AddRange(previous.Select(pe => new WorkoutPlanExercise
                {
                    WorkoutPlanDayId = day.Id,
                    ExerciseId = pe.ExerciseId,
                    Order = pe.Order,
                    Sets = pe.Sets,
                    Reps = pe.Reps,
                    RestSeconds = pe.RestSeconds,
                    DurationSeconds = pe.DurationSeconds,
                    CreatedAt = DateTime.UtcNow,
                    CreatedUser = adminUserId,
                }));
            }
            else
            {
                newPlanExercises.AddRange(catalogExercises
                    .Where(we => we.WorkoutId == day.WorkoutId)
                    .Select(we => new WorkoutPlanExercise
                    {
                        WorkoutPlanDayId = day.Id,
                        ExerciseId = we.ExerciseId,
                        Order = we.Order,
                        Sets = we.Sets,
                        Reps = we.Reps,
                        RestSeconds = we.RestSeconds,
                        DurationSeconds = we.DurationSeconds,
                        CreatedAt = DateTime.UtcNow,
                        CreatedUser = adminUserId,
                    }));
            }
        }

        if (newPlanExercises.Count > 0)
        {
            await _unitOfWork.Repository<WorkoutPlanExercise>().AddRangeAsync(newPlanExercises, cancellationToken);
        }

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_PLAN_DAYS_SET", nameof(WorkoutPlan), id, ipAddress, userAgent,
            oldValue: existing.Select(x => x.WorkoutId), newValue: workoutIds, cancellationToken: cancellationToken);

        return await GetDaysAsync(id, cancellationToken);
    }

    private static readonly string[] DayCodes = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    /// <summary>
    /// Istekteki her gun icin haftanin gununu belirler: acikca verilen deger, yoksa ayni siradaki
    /// mevcut gunun degeri, o da yoksa Pazartesi'den itibaren ilk bos gun.
    /// </summary>
    private static List<DayOfWeek> ResolveDaysOfWeek(List<AdminSetWorkoutPlanDayItem> items, List<WorkoutPlanDay> existing)
    {
        if (items.Count > 7)
        {
            throw new AppValidationException("Days", "Bir planda en fazla 7 gun olabilir.");
        }

        var result = new DayOfWeek?[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var code = items[i].DayOfWeek;
            if (code is null) continue;

            var index = Array.FindIndex(DayCodes, c => string.Equals(c, code.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new AppValidationException("Days", "Gecersiz gun. Gecerli degerler: Mon, Tue, Wed, Thu, Fri, Sat, Sun.");
            }
            result[i] = (DayOfWeek)((index + 1) % 7);
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (result[i].HasValue) continue;
            if (i < existing.Count && !result.Contains(existing[i].DayOfWeek))
            {
                result[i] = existing[i].DayOfWeek;
                continue;
            }
            result[i] = Enumerable.Range(0, 7)
                .Select(n => (DayOfWeek)((n + 1) % 7)) // Pazartesi'den baslayarak
                .First(day => !result.Contains(day));
        }

        var days = result.Select(d => d!.Value).ToList();
        if (days.Distinct().Count() != days.Count)
        {
            throw new AppValidationException("Days", "Ayni gun bir planda birden fazla kez kullanilamaz.");
        }
        return days;
    }

    private async Task<WorkoutPlan> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<WorkoutPlan>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutPlan), id);
    }
}
