using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Workouts;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>
/// IAdminWorkoutService'in varsayilan implementasyonu. Egzersiz/ekipman listeleri ayri CRUD
/// endpoint'leri yerine "tam degistirme" (replace-all) semantigiyle tek PUT uzerinden yonetilir
/// (madde 11); bu sayede istemci sirlama/silme/ekleme farkini kendisi hesaplamak zorunda kalmaz.
/// </summary>
public class AdminWorkoutService : IAdminWorkoutService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<Workout, object?>>> SortMap =
        new Dictionary<string, Expression<Func<Workout, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = x => x.Title,
            ["createdAt"] = x => x.CreatedAt,
            ["durationMin"] = x => x.DurationMin,
            ["difficulty"] = x => x.Difficulty,
            ["isActive"] = x => x.IsActive,
        };

    public AdminWorkoutService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminWorkoutListItemDto>> GetListAsync(AdminWorkoutListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Workout>().GetQueryable();
        var categories = _unitOfWork.Repository<WorkoutCategory>().GetQueryable();
        var muscleGroups = _unitOfWork.Repository<MuscleGroup>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Title.Contains(term) || x.Slug.Contains(term));
        }

        if (request.CategoryId.HasValue) query = query.Where(x => x.CategoryId == request.CategoryId.Value);
        if (request.MuscleGroupId.HasValue) query = query.Where(x => x.MuscleGroupId == request.MuscleGroupId.Value);
        if (request.Difficulty.HasValue) query = query.Where(x => x.Difficulty == request.Difficulty.Value);
        if (request.IsFeatured.HasValue) query = query.Where(x => x.IsFeatured == request.IsFeatured.Value);
        if (request.IsPremium.HasValue) query = query.Where(x => x.IsPremium == request.IsPremium.Value);
        if (request.IsAiGenerated.HasValue) query = query.Where(x => x.IsAiGenerated == request.IsAiGenerated.Value);
        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);

        var ordered = query.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "createdAt");

        var raw = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Slug,
                x.DurationMin,
                x.Difficulty,
                x.CategoryId,
                x.MuscleGroupId,
                x.ImageUrl,
                x.IsFeatured,
                x.IsPremium,
                x.IsAiGenerated,
                x.IsActive,
                x.CreatedAt,
                CategoryName = categories.Where(c => c.Id == x.CategoryId).Select(c => c.Name).FirstOrDefault(),
                MuscleGroupName = x.MuscleGroupId == null
                    ? null
                    : muscleGroups.Where(m => m.Id == x.MuscleGroupId.Value).Select(m => m.Name).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = raw.Select(x => new AdminWorkoutListItemDto
        {
            Id = x.Id,
            Title = x.Title,
            Slug = x.Slug,
            DurationMin = x.DurationMin,
            Difficulty = x.Difficulty.ToString(),
            CategoryId = x.CategoryId,
            CategoryName = x.CategoryName ?? "-",
            MuscleGroupId = x.MuscleGroupId,
            MuscleGroupName = x.MuscleGroupName,
            ImageUrl = x.ImageUrl,
            IsFeatured = x.IsFeatured,
            IsPremium = x.IsPremium,
            IsAiGenerated = x.IsAiGenerated,
            IsActive = x.IsActive,
            CreatedAt = x.CreatedAt,
        }).ToList();

        return PagedResult<AdminWorkoutListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminWorkoutDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await MapToDetailAsync(await GetOrThrowAsync(id, cancellationToken), cancellationToken);

    public async Task<AdminWorkoutDetailDto> CreateAsync(AdminUpsertWorkoutRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, existingId: null, cancellationToken);

        var entity = new Workout
        {
            Title = request.Title.Trim(),
            Slug = request.Slug.Trim().ToLowerInvariant(),
            Tagline = request.Tagline,
            Description = request.Description,
            DurationMin = request.DurationMin,
            Difficulty = request.Difficulty,
            CategoryId = request.CategoryId,
            MuscleGroupId = request.MuscleGroupId,
            ImageUrl = request.ImageUrl,
            IsFeatured = request.IsFeatured,
            IsPremium = request.IsPremium,
            IsAiGenerated = false,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.Repository<Workout>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_CREATED", nameof(Workout), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return await MapToDetailAsync(entity, cancellationToken);
    }

    public async Task<AdminWorkoutDetailDto> UpdateAsync(long id, AdminUpsertWorkoutRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, existingId: id, cancellationToken);

        var repo = _unitOfWork.Repository<Workout>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Workout), id);

        var oldValue = new { entity.Title, entity.Slug, entity.CategoryId, entity.Difficulty, entity.IsActive };

        entity.Title = request.Title.Trim();
        entity.Slug = request.Slug.Trim().ToLowerInvariant();
        entity.Tagline = request.Tagline;
        entity.Description = request.Description;
        entity.DurationMin = request.DurationMin;
        entity.Difficulty = request.Difficulty;
        entity.CategoryId = request.CategoryId;
        entity.MuscleGroupId = request.MuscleGroupId;
        entity.ImageUrl = request.ImageUrl;
        entity.IsFeatured = request.IsFeatured;
        entity.IsPremium = request.IsPremium;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_UPDATED", nameof(Workout), id, ipAddress, userAgent,
            oldValue, new { entity.Title, entity.Slug, entity.CategoryId, entity.Difficulty, entity.IsActive }, cancellationToken);

        return await MapToDetailAsync(entity, cancellationToken);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Workout>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Workout), id);

        var isUsedInPlans = await _unitOfWork.Repository<WorkoutPlanDay>().AnyAsync(x => x.WorkoutId == id, cancellationToken);
        var hasSessionHistory = await _unitOfWork.Repository<WorkoutSession>().AnyAsync(x => x.WorkoutId == id, cancellationToken);

        if (isUsedInPlans || hasSessionHistory)
        {
            throw new ConflictException("Bu antrenman kullanici planlarinda veya gecmis oturumlarda kullanildigi icin silinemez. Bunun yerine pasif hale getirebilirsiniz (PATCH /status).");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var exerciseLinks = await _unitOfWork.Repository<WorkoutExercise>().FindAsync(x => x.WorkoutId == id, cancellationToken);
        _unitOfWork.Repository<WorkoutExercise>().RemoveRange(exerciseLinks);

        var equipmentLinks = await _unitOfWork.Repository<WorkoutEquipment>().FindAsync(x => x.WorkoutId == id, cancellationToken);
        _unitOfWork.Repository<WorkoutEquipment>().RemoveRange(equipmentLinks);

        repo.Remove(entity);

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_DELETED", nameof(Workout), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateWorkoutStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Workout>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Workout), id);

        var oldValue = new { entity.IsActive };
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_STATUS_CHANGED", nameof(Workout), id, ipAddress, userAgent, oldValue, new { entity.IsActive }, cancellationToken);
    }

    public async Task UpdateFeaturedAsync(long id, AdminUpdateWorkoutFeaturedRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Workout>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Workout), id);

        var oldValue = new { entity.IsFeatured };
        entity.IsFeatured = request.IsFeatured;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_FEATURED_CHANGED", nameof(Workout), id, ipAddress, userAgent, oldValue, new { entity.IsFeatured }, cancellationToken);
    }

    public async Task UpdatePremiumAsync(long id, AdminUpdateWorkoutPremiumRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Workout>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Workout), id);

        var oldValue = new { entity.IsPremium };
        entity.IsPremium = request.IsPremium;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_PREMIUM_CHANGED", nameof(Workout), id, ipAddress, userAgent, oldValue, new { entity.IsPremium }, cancellationToken);
    }

    public async Task<List<AdminWorkoutExerciseItemDto>> GetExercisesAsync(long workoutId, CancellationToken cancellationToken = default)
    {
        await GetOrThrowAsync(workoutId, cancellationToken);

        var links = _unitOfWork.Repository<WorkoutExercise>().GetQueryable();
        var exercises = _unitOfWork.Repository<Exercise>().GetQueryable();

        return await (from l in links
                       where l.WorkoutId == workoutId
                       join e in exercises on l.ExerciseId equals e.Id
                       orderby l.Order
                       select new AdminWorkoutExerciseItemDto
                       {
                           ExerciseId = l.ExerciseId,
                           ExerciseName = e.Name,
                           Order = l.Order,
                           Sets = l.Sets,
                           Reps = l.Reps,
                           RestSeconds = l.RestSeconds,
                           DurationSeconds = l.DurationSeconds,
                           Notes = l.Notes,
                           IsOptional = l.IsOptional,
                       }).ToListAsync(cancellationToken);
    }

    public async Task<List<AdminWorkoutExerciseItemDto>> SetExercisesAsync(long workoutId, AdminSetWorkoutExercisesRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await GetOrThrowAsync(workoutId, cancellationToken);

        var exerciseIds = request.Exercises.Select(x => x.ExerciseId).ToList();

        if (exerciseIds.Count != exerciseIds.Distinct().Count())
        {
            throw new AppValidationException(nameof(request.Exercises), "Ayni egzersiz listede birden fazla kez yer alamaz.");
        }

        if (exerciseIds.Count > 0)
        {
            var existingCount = await _unitOfWork.Repository<Exercise>().GetQueryable()
                .CountAsync(e => exerciseIds.Contains(e.Id), cancellationToken);

            if (existingCount != exerciseIds.Distinct().Count())
            {
                throw new AppValidationException(nameof(request.Exercises), "Listede bulunmayan bir egzersiz Id'si var.");
            }
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var repo = _unitOfWork.Repository<WorkoutExercise>();
        var existing = await repo.FindAsync(x => x.WorkoutId == workoutId, cancellationToken);
        repo.RemoveRange(existing);

        var newLinks = request.Exercises.Select(x => new WorkoutExercise
        {
            WorkoutId = workoutId,
            ExerciseId = x.ExerciseId,
            Order = x.Order,
            Sets = x.Sets,
            Reps = x.Reps,
            RestSeconds = x.RestSeconds,
            DurationSeconds = x.DurationSeconds,
            Notes = x.Notes,
            IsOptional = x.IsOptional,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        }).ToList();

        await repo.AddRangeAsync(newLinks, cancellationToken);

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_EXERCISES_SET", nameof(Workout), workoutId, ipAddress, userAgent,
            oldValue: existing.Select(x => x.ExerciseId), newValue: exerciseIds, cancellationToken: cancellationToken);

        return await GetExercisesAsync(workoutId, cancellationToken);
    }

    public async Task<List<AdminWorkoutEquipmentItemDto>> GetEquipmentAsync(long workoutId, CancellationToken cancellationToken = default)
    {
        await GetOrThrowAsync(workoutId, cancellationToken);

        var links = _unitOfWork.Repository<WorkoutEquipment>().GetQueryable();
        var equipment = _unitOfWork.Repository<Equipment>().GetQueryable();

        return await (from l in links
                       where l.WorkoutId == workoutId
                       join e in equipment on l.EquipmentId equals e.Id
                       orderby e.Name
                       select new AdminWorkoutEquipmentItemDto
                       {
                           EquipmentId = l.EquipmentId,
                           EquipmentName = e.Name,
                       }).ToListAsync(cancellationToken);
    }

    public async Task<List<AdminWorkoutEquipmentItemDto>> SetEquipmentAsync(long workoutId, AdminSetWorkoutEquipmentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await GetOrThrowAsync(workoutId, cancellationToken);

        var equipmentIds = request.EquipmentIds.Distinct().ToList();

        if (equipmentIds.Count > 0)
        {
            var existingCount = await _unitOfWork.Repository<Equipment>().GetQueryable()
                .CountAsync(e => equipmentIds.Contains(e.Id), cancellationToken);

            if (existingCount != equipmentIds.Count)
            {
                throw new AppValidationException(nameof(request.EquipmentIds), "Listede bulunmayan bir ekipman Id'si var.");
            }
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var repo = _unitOfWork.Repository<WorkoutEquipment>();
        var existing = await repo.FindAsync(x => x.WorkoutId == workoutId, cancellationToken);
        repo.RemoveRange(existing);

        var newLinks = equipmentIds.Select(equipmentId => new WorkoutEquipment
        {
            WorkoutId = workoutId,
            EquipmentId = equipmentId,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        }).ToList();

        await repo.AddRangeAsync(newLinks, cancellationToken);

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_EQUIPMENT_SET", nameof(Workout), workoutId, ipAddress, userAgent,
            oldValue: existing.Select(x => x.EquipmentId), newValue: equipmentIds, cancellationToken: cancellationToken);

        return await GetEquipmentAsync(workoutId, cancellationToken);
    }

    private async Task ValidateAsync(AdminUpsertWorkoutRequest request, long? existingId, CancellationToken cancellationToken)
    {
        var workoutRepo = _unitOfWork.Repository<Workout>();

        var slugTaken = existingId.HasValue
            ? await workoutRepo.AnyAsync(x => x.Slug == request.Slug && x.Id != existingId.Value, cancellationToken)
            : await workoutRepo.AnyAsync(x => x.Slug == request.Slug, cancellationToken);

        if (slugTaken)
        {
            throw new AppValidationException(nameof(request.Slug), "Bu slug zaten kullaniliyor.");
        }

        if (!await _unitOfWork.Repository<WorkoutCategory>().AnyAsync(x => x.Id == request.CategoryId, cancellationToken))
        {
            throw new AppValidationException(nameof(request.CategoryId), "Belirtilen kategori bulunamadi.");
        }

        if (request.MuscleGroupId.HasValue
            && !await _unitOfWork.Repository<MuscleGroup>().AnyAsync(x => x.Id == request.MuscleGroupId.Value, cancellationToken))
        {
            throw new AppValidationException(nameof(request.MuscleGroupId), "Belirtilen kas grubu bulunamadi.");
        }
    }

    private async Task<Workout> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Workout>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Workout), id);
    }

    private async Task<AdminWorkoutDetailDto> MapToDetailAsync(Workout entity, CancellationToken cancellationToken)
    {
        var categoryName = await _unitOfWork.Repository<WorkoutCategory>().GetQueryable()
            .Where(c => c.Id == entity.CategoryId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);

        string? muscleGroupName = null;
        if (entity.MuscleGroupId.HasValue)
        {
            muscleGroupName = await _unitOfWork.Repository<MuscleGroup>().GetQueryable()
                .Where(m => m.Id == entity.MuscleGroupId.Value).Select(m => m.Name).FirstOrDefaultAsync(cancellationToken);
        }

        var exerciseCount = await _unitOfWork.Repository<WorkoutExercise>().GetQueryable()
            .CountAsync(x => x.WorkoutId == entity.Id, cancellationToken);

        var equipmentCount = await _unitOfWork.Repository<WorkoutEquipment>().GetQueryable()
            .CountAsync(x => x.WorkoutId == entity.Id, cancellationToken);

        return new AdminWorkoutDetailDto
        {
            Id = entity.Id,
            Title = entity.Title,
            Slug = entity.Slug,
            DurationMin = entity.DurationMin,
            Difficulty = entity.Difficulty.ToString(),
            CategoryId = entity.CategoryId,
            CategoryName = categoryName ?? "-",
            MuscleGroupId = entity.MuscleGroupId,
            MuscleGroupName = muscleGroupName,
            ImageUrl = entity.ImageUrl,
            IsFeatured = entity.IsFeatured,
            IsPremium = entity.IsPremium,
            IsAiGenerated = entity.IsAiGenerated,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            Tagline = entity.Tagline,
            Description = entity.Description,
            UpdatedAt = entity.UpdatedAt,
            ExerciseCount = exerciseCount,
            EquipmentCount = equipmentCount,
        };
    }
}
