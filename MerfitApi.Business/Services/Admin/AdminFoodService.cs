using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Foods;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminFoodService'in varsayilan implementasyonu.</summary>
public class AdminFoodService : IAdminFoodService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<Food, object?>>> SortMap =
        new Dictionary<string, Expression<Func<Food, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = x => x.Name,
            ["createdAt"] = x => x.CreatedAt,
            ["calories"] = x => x.Calories,
        };

    public AdminFoodService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminFoodListItemDto>> GetListAsync(AdminFoodListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Food>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Name.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            query = query.Where(x => x.Barcode != null && x.Barcode.Contains(request.Barcode));
        }

        if (!string.IsNullOrWhiteSpace(request.Brand))
        {
            query = query.Where(x => x.Brand != null && x.Brand.Contains(request.Brand));
        }

        var ordered = query.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "name");

        var mapped = ordered.Select(x => new AdminFoodListItemDto
        {
            Id = x.Id,
            Name = x.Name,
            Barcode = x.Barcode,
            Brand = x.Brand,
            ServingSize = x.ServingSize,
            ServingUnit = x.ServingUnit,
            Calories = x.Calories,
            Protein = x.Protein,
            Carbs = x.Carbs,
            Fat = x.Fat,
            ImageUrl = x.ImageUrl,
            CreatedAt = x.CreatedAt,
        });

        return await mapped.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<AdminFoodDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => MapToDetail(await GetOrThrowAsync(id, cancellationToken));

    public async Task<AdminFoodDetailDto> CreateAsync(AdminUpsertFoodRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateBarcodeAsync(request.Barcode, existingId: null, cancellationToken);

        var entity = new Food
        {
            Name = request.Name.Trim(),
            Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim(),
            Brand = request.Brand,
            ServingSize = request.ServingSize,
            ServingUnit = request.ServingUnit.Trim(),
            Calories = request.Calories,
            Protein = request.Protein,
            Carbs = request.Carbs,
            Fat = request.Fat,
            Fiber = request.Fiber,
            Sugar = request.Sugar,
            Sodium = request.Sodium,
            ImageUrl = request.ImageUrl,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.Repository<Food>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FOOD_CREATED", nameof(Food), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return MapToDetail(entity);
    }

    public async Task<AdminFoodDetailDto> UpdateAsync(long id, AdminUpsertFoodRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateBarcodeAsync(request.Barcode, existingId: id, cancellationToken);

        var repo = _unitOfWork.Repository<Food>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Food), id);

        var oldValue = new { entity.Name, entity.Calories, entity.Protein, entity.Carbs, entity.Fat };

        entity.Name = request.Name.Trim();
        entity.Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
        entity.Brand = request.Brand;
        entity.ServingSize = request.ServingSize;
        entity.ServingUnit = request.ServingUnit.Trim();
        entity.Calories = request.Calories;
        entity.Protein = request.Protein;
        entity.Carbs = request.Carbs;
        entity.Fat = request.Fat;
        entity.Fiber = request.Fiber;
        entity.Sugar = request.Sugar;
        entity.Sodium = request.Sodium;
        entity.ImageUrl = request.ImageUrl;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FOOD_UPDATED", nameof(Food), id, ipAddress, userAgent,
            oldValue, new { entity.Name, entity.Calories, entity.Protein, entity.Carbs, entity.Fat }, cancellationToken);

        return MapToDetail(entity);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Food>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Food), id);

        var isUsedInMeals = await _unitOfWork.Repository<MealItem>().AnyAsync(x => x.FoodId == id, cancellationToken);
        if (isUsedInMeals)
        {
            throw new ConflictException("Bu besin, kullanicilarin gecmis ogun kayitlarinda kullanildigi icin silinemez.");
        }

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FOOD_DELETED", nameof(Food), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    private async Task ValidateBarcodeAsync(string? barcode, long? existingId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return;
        }

        var repo = _unitOfWork.Repository<Food>();
        var taken = existingId.HasValue
            ? await repo.AnyAsync(x => x.Barcode == barcode && x.Id != existingId.Value, cancellationToken)
            : await repo.AnyAsync(x => x.Barcode == barcode, cancellationToken);

        if (taken)
        {
            throw new AppValidationException("barcode", "Bu barkod zaten kullaniliyor.");
        }
    }

    private async Task<Food> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Food>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Food), id);
    }

    private static AdminFoodDetailDto MapToDetail(Food entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Barcode = entity.Barcode,
        Brand = entity.Brand,
        ServingSize = entity.ServingSize,
        ServingUnit = entity.ServingUnit,
        Calories = entity.Calories,
        Protein = entity.Protein,
        Carbs = entity.Carbs,
        Fat = entity.Fat,
        ImageUrl = entity.ImageUrl,
        CreatedAt = entity.CreatedAt,
        Fiber = entity.Fiber,
        Sugar = entity.Sugar,
        Sodium = entity.Sodium,
        UpdatedAt = entity.UpdatedAt,
    };
}
