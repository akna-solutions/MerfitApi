using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.MuscleGroups;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminMuscleGroupService'in varsayilan implementasyonu.</summary>
public class AdminMuscleGroupService : IAdminMuscleGroupService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<MuscleGroup, object?>>> SortMap =
        new Dictionary<string, Expression<Func<MuscleGroup, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = x => x.Name,
            ["createdAt"] = x => x.CreatedAt,
        };

    public AdminMuscleGroupService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminMuscleGroupDto>> GetListAsync(AdminMuscleGroupListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<MuscleGroup>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Slug.Contains(term));
        }

        var ordered = query.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "name");

        var mapped = ordered.Select(x => new AdminMuscleGroupDto
        {
            Id = x.Id,
            Name = x.Name,
            Slug = x.Slug,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
        });

        return await mapped.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<AdminMuscleGroupDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);
        return MapToDtoInMemory(entity);
    }

    public async Task<AdminMuscleGroupDto> CreateAsync(AdminUpsertMuscleGroupRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<MuscleGroup>();

        if (await repo.AnyAsync(x => x.Slug == request.Slug, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Slug), "Bu slug zaten kullaniliyor.");
        }

        var entity = new MuscleGroup
        {
            Name = request.Name.Trim(),
            Slug = request.Slug.Trim().ToLowerInvariant(),
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await repo.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "MUSCLE_GROUP_CREATED", nameof(MuscleGroup), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return MapToDtoInMemory(entity);
    }

    public async Task<AdminMuscleGroupDto> UpdateAsync(long id, AdminUpsertMuscleGroupRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<MuscleGroup>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(MuscleGroup), id);

        if (await repo.AnyAsync(x => x.Slug == request.Slug && x.Id != id, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Slug), "Bu slug zaten kullaniliyor.");
        }

        var oldValue = new { entity.Name, entity.Slug };

        entity.Name = request.Name.Trim();
        entity.Slug = request.Slug.Trim().ToLowerInvariant();
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "MUSCLE_GROUP_UPDATED", nameof(MuscleGroup), id, ipAddress, userAgent, oldValue, new { entity.Name, entity.Slug }, cancellationToken);

        return MapToDtoInMemory(entity);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<MuscleGroup>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(MuscleGroup), id);

        var isUsedByExercise = await _unitOfWork.Repository<Exercise>().AnyAsync(x => x.PrimaryMuscleGroupId == id, cancellationToken);
        var isUsedByWorkout = await _unitOfWork.Repository<Workout>().AnyAsync(x => x.MuscleGroupId == id, cancellationToken);

        if (isUsedByExercise || isUsedByWorkout)
        {
            throw new ConflictException("Bu kas grubu mevcut egzersiz veya antrenmanlarda kullanildigi icin silinemez.");
        }

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "MUSCLE_GROUP_DELETED", nameof(MuscleGroup), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    private async Task<MuscleGroup> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<MuscleGroup>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(MuscleGroup), id);
    }

    private static AdminMuscleGroupDto MapToDtoInMemory(MuscleGroup entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Slug = entity.Slug,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };
}