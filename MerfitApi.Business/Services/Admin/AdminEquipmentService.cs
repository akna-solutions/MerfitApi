using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Equipment;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminEquipmentService'in varsayilan implementasyonu.</summary>
public class AdminEquipmentService : IAdminEquipmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<Equipment, object?>>> SortMap =
        new Dictionary<string, Expression<Func<Equipment, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = x => x.Name,
            ["createdAt"] = x => x.CreatedAt,
        };

    public AdminEquipmentService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminEquipmentDto>> GetListAsync(AdminEquipmentListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Equipment>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Slug.Contains(term));
        }

        var ordered = query.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "name");

        var mapped = ordered.Select(x => new AdminEquipmentDto
        {
            Id = x.Id,
            Name = x.Name,
            Slug = x.Slug,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
        });

        return await mapped.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<AdminEquipmentDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);
        return MapToDto(entity);
    }

    public async Task<AdminEquipmentDto> CreateAsync(AdminUpsertEquipmentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Equipment>();

        if (await repo.AnyAsync(x => x.Slug == request.Slug, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Slug), "Bu slug zaten kullaniliyor.");
        }

        var entity = new Equipment
        {
            Name = request.Name.Trim(),
            Slug = request.Slug.Trim().ToLowerInvariant(),
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await repo.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "EQUIPMENT_CREATED", nameof(Equipment), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return MapToDto(entity);
    }

    public async Task<AdminEquipmentDto> UpdateAsync(long id, AdminUpsertEquipmentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Equipment>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Equipment), id);

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

        await _auditLogService.LogAsync(adminUserId, "EQUIPMENT_UPDATED", nameof(Equipment), id, ipAddress, userAgent, oldValue, new { entity.Name, entity.Slug }, cancellationToken);

        return MapToDto(entity);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Equipment>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Equipment), id);

        var isUsedByWorkout = await _unitOfWork.Repository<WorkoutEquipment>().AnyAsync(x => x.EquipmentId == id, cancellationToken);
        var isUsedByUser = await _unitOfWork.Repository<UserEquipment>().AnyAsync(x => x.EquipmentId == id, cancellationToken);

        if (isUsedByWorkout || isUsedByUser)
        {
            throw new ConflictException("Bu ekipman mevcut antrenman veya kullanici kayitlarinda kullanildigi icin silinemez.");
        }

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "EQUIPMENT_DELETED", nameof(Equipment), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    private async Task<Equipment> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Equipment>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Equipment), id);
    }

    private static AdminEquipmentDto MapToDto(Equipment entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Slug = entity.Slug,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };
}
