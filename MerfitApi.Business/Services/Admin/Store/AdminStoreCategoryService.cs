using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin.Store;

/// <summary>
/// MB Shop kategorileri. Pasif kategorideki urunler mobilde listelenmez. Urunu veya alt kategorisi olan kategori
/// silinemez (once tasinmali ya da kategori pasife alinmalidir).
/// </summary>
public class AdminStoreCategoryService : IAdminStoreCategoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;
    private readonly TimeProvider _timeProvider;

    public AdminStoreCategoryService(IUnitOfWork unitOfWork, IAuditLogService auditLogService, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<AdminStoreCategoryDto>> GetListAsync(AdminStoreCategoryListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<StoreCategory>().GetQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term) || c.Slug.Contains(term));
        }

        if (request.IsActive.HasValue) query = query.Where(c => c.IsActive == request.IsActive.Value);

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = await ToDtosAsync(rows, cancellationToken);
        return PagedResult<AdminStoreCategoryDto>.Create(dtos, request.Page, request.PageSize, total);
    }

    public async Task<AdminStoreCategoryDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => (await ToDtosAsync(new List<StoreCategory> { await GetOrThrowAsync(id, tracking: false, cancellationToken) }, cancellationToken))[0];

    public async Task<AdminStoreCategoryDto> CreateAsync(AdminUpsertStoreCategoryRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = new StoreCategory { CreatedAt = UtcNow, CreatedUser = adminUserId };
        await ApplyAsync(entity, request, cancellationToken);

        await _unitOfWork.Repository<StoreCategory>().AddAsync(entity, cancellationToken);
        await SaveAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "STORE_CATEGORY_CREATED", nameof(StoreCategory), entity.Id, ipAddress, userAgent,
            newValue: Snapshot(entity), cancellationToken: cancellationToken);
        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<AdminStoreCategoryDto> UpdateAsync(long id, AdminUpsertStoreCategoryRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, tracking: true, cancellationToken);
        var oldValue = Snapshot(entity);

        await ApplyAsync(entity, request, cancellationToken);
        entity.UpdatedAt = UtcNow;
        entity.UpdatedUser = adminUserId;
        await SaveAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "STORE_CATEGORY_UPDATED", nameof(StoreCategory), id, ipAddress, userAgent,
            oldValue, Snapshot(entity), cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateStoreStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, tracking: true, cancellationToken);
        var oldValue = new { entity.IsActive };
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = UtcNow;
        entity.UpdatedUser = adminUserId;
        await SaveAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "STORE_CATEGORY_STATUS_CHANGED", nameof(StoreCategory), id, ipAddress, userAgent,
            oldValue, new { entity.IsActive }, cancellationToken);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, tracking: true, cancellationToken);
        if (await _unitOfWork.Repository<Product>().GetQueryable().AnyAsync(p => p.CategoryId == id, cancellationToken))
        {
            throw new ConflictException("Bu kategoride urun var. Urunleri baska kategoriye tasiyin veya kategoriyi pasife alin.");
        }

        if (await _unitOfWork.Repository<StoreCategory>().GetQueryable().AnyAsync(c => c.ParentCategoryId == id, cancellationToken))
        {
            throw new ConflictException("Bu kategorinin alt kategorileri var. Once alt kategorileri tasiyin veya silin.");
        }

        var oldValue = Snapshot(entity);
        _unitOfWork.Repository<StoreCategory>().Remove(entity);
        await SaveAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "STORE_CATEGORY_DELETED", nameof(StoreCategory), id, ipAddress, userAgent,
            oldValue, cancellationToken: cancellationToken);
    }

    private async Task ApplyAsync(StoreCategory entity, AdminUpsertStoreCategoryRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length < 2)
        {
            throw new AppValidationException(nameof(request.Name), "Kategori adi zorunludur.");
        }

        if (request.ParentCategoryId.HasValue)
        {
            var parentId = request.ParentCategoryId.Value;
            if (entity.Id != 0 && parentId == entity.Id)
            {
                throw new AppValidationException(nameof(request.ParentCategoryId), "Kategori kendisinin alt kategorisi olamaz.");
            }

            var parent = await _unitOfWork.Repository<StoreCategory>().GetQueryable()
                .FirstOrDefaultAsync(c => c.Id == parentId, cancellationToken)
                ?? throw new AppValidationException(nameof(request.ParentCategoryId), "Ust kategori bulunamadi.");

            // Mobil uygulama iki seviyeyi gosterir; derin agac ve dongu olusmamasi icin ust kategori kok olmalidir.
            if (parent.ParentCategoryId.HasValue)
            {
                throw new AppValidationException(nameof(request.ParentCategoryId), "Ust kategori olarak yalnizca ana kategoriler secilebilir.");
            }

            if (entity.Id != 0 && await _unitOfWork.Repository<StoreCategory>().GetQueryable().AnyAsync(c => c.ParentCategoryId == entity.Id, cancellationToken))
            {
                throw new AppValidationException(nameof(request.ParentCategoryId), "Alt kategorisi olan bir kategori baska kategorinin altina tasinamaz.");
            }
        }

        entity.Name = name;
        entity.Slug = AdminStoreInput.Slug(request.Slug, name, 140, nameof(request.Slug));
        entity.Description = AdminStoreInput.NullIfBlank(request.Description);
        entity.ImageUrl = AdminStoreInput.HttpsUrl(request.ImageUrl, nameof(request.ImageUrl));
        entity.Icon = AdminStoreInput.NullIfBlank(request.Icon);
        entity.ParentCategoryId = request.ParentCategoryId;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
    }

    private Task SaveAsync(CancellationToken cancellationToken) =>
        AdminStoreInput.SaveAsync(_unitOfWork, _ => "Bu slug baska bir kategoride kullaniliyor.", cancellationToken);

    private async Task<StoreCategory> GetOrThrowAsync(long id, bool tracking, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<StoreCategory>().GetQueryable(asNoTracking: !tracking).FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
        ?? throw new NotFoundException("Kategori", id);

    private async Task<List<AdminStoreCategoryDto>> ToDtosAsync(List<StoreCategory> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var parentIds = rows.Where(r => r.ParentCategoryId.HasValue).Select(r => r.ParentCategoryId!.Value).Distinct().ToList();
        var counts = await _unitOfWork.Repository<Product>().GetQueryable()
            .Where(p => ids.Contains(p.CategoryId))
            .GroupBy(p => p.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var parents = await _unitOfWork.Repository<StoreCategory>().GetQueryable()
            .Where(c => parentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return rows.Select(c => new AdminStoreCategoryDto
        {
            Id = c.Id,
            Name = c.Name,
            Slug = c.Slug,
            Description = c.Description,
            ImageUrl = c.ImageUrl,
            Icon = c.Icon,
            ParentCategoryId = c.ParentCategoryId,
            ParentCategoryName = c.ParentCategoryId.HasValue ? parents.GetValueOrDefault(c.ParentCategoryId.Value) : null,
            IsActive = c.IsActive,
            SortOrder = c.SortOrder,
            ProductCount = counts.GetValueOrDefault(c.Id),
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
        }).ToList();
    }

    private static object Snapshot(StoreCategory c) => new { c.Name, c.Slug, c.ParentCategoryId, c.IsActive, c.SortOrder, c.Icon, c.ImageUrl };
}
