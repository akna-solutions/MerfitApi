using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Exercises;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
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

        if (isUsedByWorkout || hasSessionHistory || hasPersonalRecords)
        {
            throw new ConflictException("Bu egzersiz mevcut antrenman veya kullanici gecmisinde kullanildigi icin silinemez. Bunun yerine pasif hale getirebilirsiniz (PATCH /status).");
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
        };
    }
}
