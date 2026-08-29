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

        return await (from d in days
                       where d.WorkoutPlanId == id
                       join w in workouts on d.WorkoutId equals w.Id
                       orderby d.Order
                       select new AdminWorkoutPlanDayItemDto
                       {
                           WorkoutId = d.WorkoutId,
                           WorkoutTitle = w.Title,
                           Order = d.Order,
                       }).ToListAsync(cancellationToken);
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

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var repo = _unitOfWork.Repository<WorkoutPlanDay>();
        var existing = await repo.FindAsync(x => x.WorkoutPlanId == id, cancellationToken);
        repo.RemoveRange(existing);

        var newDays = request.Days.Select(x => new WorkoutPlanDay
        {
            WorkoutPlanId = id,
            WorkoutId = x.WorkoutId,
            Order = x.Order,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        }).ToList();

        await repo.AddRangeAsync(newDays, cancellationToken);

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_PLAN_DAYS_SET", nameof(WorkoutPlan), id, ipAddress, userAgent,
            oldValue: existing.Select(x => x.WorkoutId), newValue: workoutIds, cancellationToken: cancellationToken);

        return await GetDaysAsync(id, cancellationToken);
    }

    private async Task<WorkoutPlan> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<WorkoutPlan>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutPlan), id);
    }
}
