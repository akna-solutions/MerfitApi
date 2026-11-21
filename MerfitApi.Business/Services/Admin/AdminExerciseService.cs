using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Exercises;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminExerciseService'in varsayilan implementasyonu.</summary>
public class AdminExerciseService : IAdminExerciseService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<Exercise, object?>>> SortMap =
        new Dictionary<string, Expression<Func<Exercise, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = x => x.Name,
            ["createdAt"] = x => x.CreatedAt,
            ["difficulty"] = x => x.Difficulty,
            ["isActive"] = x => x.IsActive,
        };

    public AdminExerciseService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminExerciseListItemDto>> GetListAsync(AdminExerciseListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Exercise>().GetQueryable();
        var muscleGroups = _unitOfWork.Repository<MuscleGroup>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Slug.Contains(term));
        }

        if (request.Difficulty.HasValue)
        {
            query = query.Where(x => x.Difficulty == request.Difficulty.Value);
        }

        if (request.MuscleGroupId.HasValue)
        {
            query = query.Where(x => x.PrimaryMuscleGroupId == request.MuscleGroupId.Value);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == request.IsActive.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        var ordered = query.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "name");

        var raw = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Slug,
                x.Difficulty,
                x.PrimaryMuscleGroupId,
                x.ImageUrl,
                x.IsActive,
                x.CreatedAt,
                MuscleGroupName = muscleGroups.Where(m => m.Id == x.PrimaryMuscleGroupId).Select(m => m.Name).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = raw.Select(x => new AdminExerciseListItemDto
        {
            Id = x.Id,
            Name = x.Name,
            Slug = x.Slug,
            Difficulty = x.Difficulty.ToString(),
            PrimaryMuscleGroupId = x.PrimaryMuscleGroupId,
            PrimaryMuscleGroupName = x.MuscleGroupName ?? "-",
            ImageUrl = x.ImageUrl,
            IsActive = x.IsActive,
            CreatedAt = x.CreatedAt,
        }).ToList();

        return PagedResult<AdminExerciseListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminExerciseDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);
        return await MapToDetailAsync(entity, cancellationToken);
    }

    public async Task<AdminExerciseDetailDto> CreateAsync(AdminUpsertExerciseRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, existingId: null, cancellationToken);

        var entity = new Exercise
        {
            Name = request.Name.Trim(),
            Slug = request.Slug.Trim().ToLowerInvariant(),
            Description = request.Description,
            Instructions = request.Instructions,
            Difficulty = request.Difficulty,
            PrimaryMuscleGroupId = request.PrimaryMuscleGroupId,
            EquipmentType = request.EquipmentType,
            VideoUrl = request.VideoUrl,
            ImageUrl = request.ImageUrl,
            CaloriesPerMinute = request.CaloriesPerMinute,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.Repository<Exercise>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "EXERCISE_CREATED", nameof(Exercise), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return await MapToDetailAsync(entity, cancellationToken);
    }

    public async Task<AdminExerciseDetailDto> UpdateAsync(long id, AdminUpsertExerciseRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, existingId: id, cancellationToken);

        var repo = _unitOfWork.Repository<Exercise>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Exercise), id);

        var oldValue = new { entity.Name, entity.Slug, entity.Difficulty, entity.PrimaryMuscleGroupId, entity.IsActive };

        entity.Name = request.Name.Trim();
        entity.Slug = request.Slug.Trim().ToLowerInvariant();
        entity.Description = request.Description;
        entity.Instructions = request.Instructions;
        entity.Difficulty = request.Difficulty;
        entity.PrimaryMuscleGroupId = request.PrimaryMuscleGroupId;
        entity.EquipmentType = request.EquipmentType;
        entity.VideoUrl = request.VideoUrl;
        entity.ImageUrl = request.ImageUrl;
        entity.CaloriesPerMinute = request.CaloriesPerMinute;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "EXERCISE_UPDATED", nameof(Exercise), id, ipAddress, userAgent,
            oldValue, new { entity.Name, entity.Slug, entity.Difficulty, entity.PrimaryMuscleGroupId, entity.IsActive }, cancellationToken);

        return await MapToDetailAsync(entity, cancellationToken);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Exercise>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Exercise), id);

        var isUsedByWorkout = await _unitOfWork.Repository<WorkoutExercise>().AnyAsync(x => x.ExerciseId == id, cancellationToken);
        var hasSessionHistory = await _unitOfWork.Repository<WorkoutSessionExercise>().AnyAsync(x => x.ExerciseId == id, cancellationToken);
        var hasPersonalRecords = await _unitOfWork.Repository<PersonalRecord>().AnyAsync(x => x.ExerciseId == id, cancellationToken);
        // Kullanicilara uretilen antrenman programlari (WorkoutPlanExercise) egzersize Restrict FK ile baglidir.
        var isUsedInPlans = await _unitOfWork.Repository<WorkoutPlanExercise>().AnyAsync(x => x.ExerciseId == id, cancellationToken);

        if (isUsedByWorkout || hasSessionHistory || hasPersonalRecords || isUsedInPlans)
        {
            throw new ConflictException("Bu egzersiz mevcut antrenman, kullanici programi veya kullanici gecmisinde kullanildigi icin silinemez. Bunun yerine pasif hale getirebilirsiniz (PATCH /status).");
        }

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "EXERCISE_DELETED", nameof(Exercise), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateExerciseStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Exercise>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Exercise), id);

        var oldValue = new { entity.IsActive };
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "EXERCISE_STATUS_CHANGED", nameof(Exercise), id, ipAddress, userAgent, oldValue, new { entity.IsActive }, cancellationToken);
    }

    public async Task<AdminExerciseDetailDto> UpdateTrainingMetadataAsync(long id, AdminUpdateExerciseTrainingMetadataRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Exercise>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Exercise), id);

        if (request.MechanicType.HasValue && !Enum.IsDefined(request.MechanicType.Value))
        {
            throw new AppValidationException(nameof(request.MechanicType), "Geçersiz mekanik tipi.");
        }
        if (request.MovementPattern.HasValue && !Enum.IsDefined(request.MovementPattern.Value))
        {
            throw new AppValidationException(nameof(request.MovementPattern), "Geçersiz hareket paterni.");
        }

        var secondary = request.SecondaryMuscleGroupIds.Distinct().Where(m => m != entity.PrimaryMuscleGroupId).ToList();
        if (secondary.Count > 0 && await _unitOfWork.Repository<MuscleGroup>().GetQueryable().CountAsync(m => secondary.Contains(m.Id), cancellationToken) != secondary.Count)
        {
            throw new AppValidationException(nameof(request.SecondaryMuscleGroupIds), "Belirtilen kas gruplarından biri bulunamadı.");
        }

        var equipment = request.EquipmentIds.Distinct().ToList();
        if (equipment.Count > 0 && await _unitOfWork.Repository<Equipment>().GetQueryable().CountAsync(e => equipment.Contains(e.Id), cancellationToken) != equipment.Count)
        {
            throw new AppValidationException(nameof(request.EquipmentIds), "Belirtilen ekipmanlardan biri bulunamadı.");
        }

        var muscleRepo = _unitOfWork.Repository<ExerciseMuscleGroup>();
        var equipmentRepo = _unitOfWork.Repository<ExerciseEquipment>();
        var oldMuscles = await muscleRepo.GetQueryable(asNoTracking: false).Where(x => x.ExerciseId == id).ToListAsync(cancellationToken);
        var oldEquipment = await equipmentRepo.GetQueryable(asNoTracking: false).Where(x => x.ExerciseId == id).ToListAsync(cancellationToken);
        var oldValue = new
        {
            entity.MechanicType, entity.MovementPattern, entity.LoadIncrementKg,
            Secondary = oldMuscles.Select(m => m.MuscleGroupId).OrderBy(x => x).ToList(),
            Equipment = oldEquipment.Select(e => e.EquipmentId).OrderBy(x => x).ToList(),
        };

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        entity.MechanicType = request.MechanicType;
        entity.MovementPattern = request.MovementPattern;
        entity.LoadIncrementKg = request.LoadIncrementKg;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;
        repo.Update(entity);

        foreach (var row in oldMuscles.Where(m => m.Role != ExerciseMuscleRole.Secondary || !secondary.Contains(m.MuscleGroupId)))
        {
            muscleRepo.Remove(row);
        }
        var keptMuscles = oldMuscles.Where(m => m.Role == ExerciseMuscleRole.Secondary).Select(m => m.MuscleGroupId).ToHashSet();
        foreach (var muscleId in secondary.Where(m => !keptMuscles.Contains(m)))
        {
            await muscleRepo.AddAsync(new ExerciseMuscleGroup { ExerciseId = id, MuscleGroupId = muscleId, Role = ExerciseMuscleRole.Secondary, CreatedAt = DateTime.UtcNow, CreatedUser = adminUserId }, cancellationToken);
        }

        foreach (var row in oldEquipment.Where(e => !equipment.Contains(e.EquipmentId)))
        {
            equipmentRepo.Remove(row);
        }
        var keptEquipment = oldEquipment.Select(e => e.EquipmentId).ToHashSet();
        foreach (var equipmentId in equipment.Where(e => !keptEquipment.Contains(e)))
        {
            await equipmentRepo.AddAsync(new ExerciseEquipment { ExerciseId = id, EquipmentId = equipmentId, CreatedAt = DateTime.UtcNow, CreatedUser = adminUserId }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "EXERCISE_TRAINING_METADATA_UPDATED", nameof(Exercise), id, ipAddress, userAgent, oldValue,
            new { entity.MechanicType, entity.MovementPattern, entity.LoadIncrementKg, Secondary = secondary, Equipment = equipment }, cancellationToken);

        return await MapToDetailAsync(entity, cancellationToken);
    }

    public async Task<AdminExerciseMetadataAuditDto> GetMetadataAuditAsync(CancellationToken cancellationToken = default)
    {
        var exercises = await _unitOfWork.Repository<Exercise>().GetQueryable()
            .Where(e => e.IsActive)
            .Select(e => new { e.Id, e.Name, e.PrimaryMuscleGroupId, e.MechanicType, e.MovementPattern, e.LoadIncrementKg, e.EquipmentType, e.ImageUrl, e.VideoUrl })
            .ToListAsync(cancellationToken);
        var ids = exercises.Select(e => e.Id).ToList();
        var withSecondary = (await _unitOfWork.Repository<ExerciseMuscleGroup>().GetQueryable()
            .Where(l => ids.Contains(l.ExerciseId) && l.Role == ExerciseMuscleRole.Secondary).Select(l => l.ExerciseId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var withEquipment = (await _unitOfWork.Repository<ExerciseEquipment>().GetQueryable()
            .Where(l => ids.Contains(l.ExerciseId)).Select(l => l.ExerciseId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var muscles = await _unitOfWork.Repository<MuscleGroup>().GetQueryable().Select(m => new { m.Id, m.Name }).ToListAsync(cancellationToken);
        var muscleNames = muscles.ToDictionary(m => m.Id, m => m.Name);

        var items = exercises.Select(e =>
        {
            var missing = new List<string>();
            if (e.MechanicType is null) missing.Add("MechanicType");
            if (e.MovementPattern is null) missing.Add("MovementPattern");
            if (e.LoadIncrementKg is null) missing.Add("LoadIncrementKg");
            // Izolasyon hareketlerinde yardimci kas beklenmez.
            if (e.MechanicType != ExerciseMechanicType.Isolation && !withSecondary.Contains(e.Id)) missing.Add("SecondaryMuscles");
            if (!withEquipment.Contains(e.Id)) missing.Add("EquipmentLinks");
            if (string.IsNullOrWhiteSpace(e.EquipmentType)) missing.Add("EquipmentType");
            if (string.IsNullOrWhiteSpace(e.ImageUrl) && string.IsNullOrWhiteSpace(e.VideoUrl)) missing.Add("Media");
            return new AdminExerciseMetadataAuditItemDto
            {
                Id = e.Id,
                Name = e.Name,
                PrimaryMuscleGroupName = muscleNames.GetValueOrDefault(e.PrimaryMuscleGroupId, "-"),
                Missing = missing,
            };
        }).ToList();

        int Count(string field) => items.Count(i => i.Missing.Contains(field));
        return new AdminExerciseMetadataAuditDto
        {
            ActiveExerciseCount = exercises.Count,
            MissingMechanicType = Count("MechanicType"),
            MissingMovementPattern = Count("MovementPattern"),
            MissingLoadIncrement = Count("LoadIncrementKg"),
            MissingSecondaryMuscles = Count("SecondaryMuscles"),
            MissingEquipmentLinks = Count("EquipmentLinks"),
            MissingEquipmentTypeText = Count("EquipmentType"),
            MissingImageOrVideo = Count("Media"),
            ByMuscleGroup = exercises.GroupBy(e => e.PrimaryMuscleGroupId)
                .Select(g => new AdminExerciseMetadataAuditMuscleDto { MuscleGroupId = g.Key, MuscleGroupName = muscleNames.GetValueOrDefault(g.Key, "-"), ActiveExerciseCount = g.Count() })
                .OrderByDescending(m => m.ActiveExerciseCount).ThenBy(m => m.MuscleGroupName)
                .ToList(),
            Items = items.Where(i => i.Missing.Count > 0).OrderByDescending(i => i.Missing.Count).ThenBy(i => i.Id).Take(200).ToList(),
        };
    }

    private async Task ValidateAsync(AdminUpsertExerciseRequest request, long? existingId, CancellationToken cancellationToken)
    {
        var exerciseRepo = _unitOfWork.Repository<Exercise>();

        var slugTaken = existingId.HasValue
            ? await exerciseRepo.AnyAsync(x => x.Slug == request.Slug && x.Id != existingId.Value, cancellationToken)
            : await exerciseRepo.AnyAsync(x => x.Slug == request.Slug, cancellationToken);

        if (slugTaken)
        {
            throw new AppValidationException(nameof(request.Slug), "Bu slug zaten kullaniliyor.");
        }

        var muscleGroupExists = await _unitOfWork.Repository<MuscleGroup>().AnyAsync(x => x.Id == request.PrimaryMuscleGroupId, cancellationToken);
        if (!muscleGroupExists)
        {
            throw new AppValidationException(nameof(request.PrimaryMuscleGroupId), "Belirtilen kas grubu bulunamadi.");
        }
    }

    private async Task<Exercise> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Exercise>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Exercise), id);
    }

    private async Task<AdminExerciseDetailDto> MapToDetailAsync(Exercise entity, CancellationToken cancellationToken)
    {
        var muscleGroupName = await _unitOfWork.Repository<MuscleGroup>().GetQueryable()
            .Where(m => m.Id == entity.PrimaryMuscleGroupId)
            .Select(m => m.Name)
            .FirstOrDefaultAsync(cancellationToken);
        var secondaryIds = await _unitOfWork.Repository<ExerciseMuscleGroup>().GetQueryable()
            .Where(l => l.ExerciseId == entity.Id && l.Role == ExerciseMuscleRole.Secondary)
            .Select(l => l.MuscleGroupId).ToListAsync(cancellationToken);
        var secondary = await _unitOfWork.Repository<MuscleGroup>().GetQueryable()
            .Where(m => secondaryIds.Contains(m.Id)).OrderBy(m => m.Id).Select(m => new { m.Id, m.Name }).ToListAsync(cancellationToken);
        var equipmentIds = await _unitOfWork.Repository<ExerciseEquipment>().GetQueryable()
            .Where(l => l.ExerciseId == entity.Id).Select(l => l.EquipmentId).ToListAsync(cancellationToken);
        var equipment = await _unitOfWork.Repository<Equipment>().GetQueryable()
            .Where(e => equipmentIds.Contains(e.Id)).OrderBy(e => e.Id).Select(e => new { e.Id, e.Name }).ToListAsync(cancellationToken);

        return new AdminExerciseDetailDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Slug = entity.Slug,
            Difficulty = entity.Difficulty.ToString(),
            PrimaryMuscleGroupId = entity.PrimaryMuscleGroupId,
            PrimaryMuscleGroupName = muscleGroupName ?? "-",
            ImageUrl = entity.ImageUrl,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            Description = entity.Description,
            Instructions = entity.Instructions,
            EquipmentType = entity.EquipmentType,
            VideoUrl = entity.VideoUrl,
            CaloriesPerMinute = entity.CaloriesPerMinute,
            UpdatedAt = entity.UpdatedAt,
            TrainingMetadata = new AdminExerciseTrainingMetadataDto
            {
                MechanicType = entity.MechanicType?.ToString(),
                MovementPattern = entity.MovementPattern?.ToString(),
                LoadIncrementKg = entity.LoadIncrementKg,
                SecondaryMuscleGroupIds = secondary.Select(m => m.Id).ToList(),
                SecondaryMuscleGroupNames = secondary.Select(m => m.Name).ToList(),
                EquipmentIds = equipment.Select(e => e.Id).ToList(),
                EquipmentNames = equipment.Select(e => e.Name).ToList(),
            },
        };
    }
}
