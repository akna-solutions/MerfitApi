using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.WorkoutCategories;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminWorkoutCategoryService'in varsayilan implementasyonu.</summary>
public class AdminWorkoutCategoryService : IAdminWorkoutCategoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<WorkoutCategory, object?>>> SortMap =
        new Dictionary<string, Expression<Func<WorkoutCategory, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = x => x.Name,
            ["createdAt"] = x => x.CreatedAt,
            ["isActive"] = x => x.IsActive,
        };

    public AdminWorkoutCategoryService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminWorkoutCategoryDto>> GetListAsync(AdminWorkoutCategoryListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<WorkoutCategory>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Slug.Contains(term));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == request.IsActive.Value);
        }

        var ordered = query.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "name");

        var mapped = ordered.Select(x => new AdminWorkoutCategoryDto
        {
            Id = x.Id,
            Name = x.Name,
            Slug = x.Slug,
            Description = x.Description,
            ImageUrl = x.ImageUrl,
            IsActive = x.IsActive,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
        });

        return await mapped.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<AdminWorkoutCategoryDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => MapToDto(await GetOrThrowAsync(id, cancellationToken));

    public async Task<AdminWorkoutCategoryDto> CreateAsync(AdminUpsertWorkoutCategoryRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<WorkoutCategory>();

        if (await repo.AnyAsync(x => x.Slug == request.Slug, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Slug), "Bu slug zaten kullaniliyor.");
        }

        var entity = new WorkoutCategory
        {
            Name = request.Name.Trim(),
            Slug = request.Slug.Trim().ToLowerInvariant(),
            Description = request.Description,
            ImageUrl = request.ImageUrl,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await repo.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_CATEGORY_CREATED", nameof(WorkoutCategory), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return MapToDto(entity);
    }

    public async Task<AdminWorkoutCategoryDto> UpdateAsync(long id, AdminUpsertWorkoutCategoryRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<WorkoutCategory>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutCategory), id);

        if (await repo.AnyAsync(x => x.Slug == request.Slug && x.Id != id, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Slug), "Bu slug zaten kullaniliyor.");
        }

        var oldValue = new { entity.Name, entity.Slug, entity.IsActive };

        entity.Name = request.Name.Trim();
        entity.Slug = request.Slug.Trim().ToLowerInvariant();
        entity.Description = request.Description;
        entity.ImageUrl = request.ImageUrl;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_CATEGORY_UPDATED", nameof(WorkoutCategory), id, ipAddress, userAgent,
            oldValue, new { entity.Name, entity.Slug, entity.IsActive }, cancellationToken);

        return MapToDto(entity);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<WorkoutCategory>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutCategory), id);

        var isUsedByWorkout = await _unitOfWork.Repository<Workout>().AnyAsync(x => x.CategoryId == id, cancellationToken);
        if (isUsedByWorkout)
        {
            throw new ConflictException("Bu kategori mevcut antrenmanlarda kullanildigi icin silinemez. Once ilgili antrenmanlarin kategorisini degistirin.");
        }

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_CATEGORY_DELETED", nameof(WorkoutCategory), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    private async Task<WorkoutCategory> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<WorkoutCategory>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutCategory), id);
    }

    private static AdminWorkoutCategoryDto MapToDto(WorkoutCategory entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Slug = entity.Slug,
        Description = entity.Description,
        ImageUrl = entity.ImageUrl,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };
}
