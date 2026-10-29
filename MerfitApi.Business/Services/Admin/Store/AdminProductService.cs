using System.Text.Json;
using System.Text.RegularExpressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin.Store;

/// <summary>
/// MB Shop urunleri. Stok, PUT ile degil PATCH /stock ile tek bir kosullu UPDATE olarak degistirilir; boylece ayni
/// anda verilen siparislerin dustugu stok ezilmez ve stok 0'in altina inemez. Siparislerde kullanilmis urun silinemez,
/// pasife alinir (siparis kalemleri zaten ad/SKU/fiyat kopyasini tutar).
/// </summary>
public partial class AdminProductService : IAdminProductService
{
    private static readonly HashSet<string> VariantKinds = new(StringComparer.Ordinal) { "size", "color", "package", "option" };

    [GeneratedRegex("^[a-z0-9_-]{1,40}$")]
    private static partial Regex VariantIdPattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex SwatchPattern();

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;
    private readonly TimeProvider _timeProvider;

    public AdminProductService(IUnitOfWork unitOfWork, IAuditLogService auditLogService, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<AdminProductListItemDto>> GetListAsync(AdminProductListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Product>().GetQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term) || p.Sku.ToLower().Contains(term) || p.Brand.ToLower().Contains(term));
        }

        if (request.CategoryId.HasValue) query = query.Where(p => p.CategoryId == request.CategoryId.Value);
        if (request.IsActive.HasValue) query = query.Where(p => p.IsActive == request.IsActive.Value);
        if (request.IsFeatured.HasValue) query = query.Where(p => p.IsFeatured == request.IsFeatured.Value);
        if (request.OutOfStock == true) query = query.Where(p => p.StockQuantity == 0);
        if (request.OutOfStock == false) query = query.Where(p => p.StockQuantity > 0);
        if (request.LowStockThreshold.HasValue) query = query.Where(p => p.StockQuantity <= request.LowStockThreshold.Value);

        var total = await query.LongCountAsync(cancellationToken);
        var desc = request.IsDescending;
        IOrderedQueryable<Product> ordered = request.SortBy?.Trim().ToLowerInvariant() switch
        {
            "name" => desc ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
            "price" => desc ? query.OrderByDescending(p => p.DiscountPrice ?? p.Price) : query.OrderBy(p => p.DiscountPrice ?? p.Price),
            "stock" or "stockquantity" => desc ? query.OrderByDescending(p => p.StockQuantity) : query.OrderBy(p => p.StockQuantity),
            "createdat" => desc ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            "sortorder" => desc ? query.OrderByDescending(p => p.SortOrder) : query.OrderBy(p => p.SortOrder),
            null or "" => query.OrderByDescending(p => p.CreatedAt),
            _ => throw new AppValidationException(nameof(request.SortBy), "Gecersiz siralama alani."),
        };

        var rows = await ordered.ThenByDescending(p => p.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = await ToListItemsAsync(rows, cancellationToken);
        return PagedResult<AdminProductListItemDto>.Create(items, request.Page, request.PageSize, total);
    }

    public async Task<AdminProductDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().GetQueryable()
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Urun", id);

        var listItem = (await ToListItemsAsync(new List<Product> { product }, cancellationToken))[0];
        var dto = new AdminProductDto();
        foreach (var property in typeof(AdminProductListItemDto).GetProperties())
        {
            property.SetValue(dto, property.GetValue(listItem));
        }

        dto.Description = product.Description;
        dto.Features = Read<List<string>>(product.FeaturesJson) ?? new();
        dto.Specs = Read<List<AdminProductSpecDto>>(product.SpecsJson) ?? new();
        dto.Variants = Read<List<AdminProductVariantGroupDto>>(product.VariantsJson) ?? new();
        dto.Images = product.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new AdminProductImageDto { Id = i.Id, ImageUrl = i.ImageUrl, SortOrder = i.SortOrder, IsPrimary = i.IsPrimary })
            .ToList();
        return dto;
    }

    public async Task<AdminProductDto> CreateAsync(AdminUpsertProductRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var product = new Product { CreatedAt = UtcNow, CreatedUser = adminUserId, StockQuantity = request.StockQuantity };
        if (request.StockQuantity < 0)
        {
            throw new AppValidationException(nameof(request.StockQuantity), "Stok negatif olamaz.");
        }

        var imageUrls = await ApplyAsync(product, request, cancellationToken);
        SyncImages(product, imageUrls);
        await _unitOfWork.Repository<Product>().AddAsync(product, cancellationToken);
        await SaveAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "STORE_PRODUCT_CREATED", nameof(Product), product.Id, ipAddress, userAgent,
            newValue: Snapshot(product), cancellationToken: cancellationToken);
        return await GetByIdAsync(product.Id, cancellationToken);
    }

    public async Task<AdminProductDto> UpdateAsync(long id, AdminUpsertProductRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().GetQueryable(asNoTracking: false)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Urun", id);
        var oldValue = Snapshot(product);

        var imageUrls = await ApplyAsync(product, request, cancellationToken);
        product.UpdatedAt = UtcNow;
        product.UpdatedUser = adminUserId;

        // Ana gorsel tekil index'i (urun basina tek IsPrimary) ara adimda ihlal edilmesin diye ana gorsel degisiyorsa
        // once eski ana gorselin bayragi kapatilip kaydedilir, sonra liste esitlenir; ikisi tek transaction'dadir.
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (ClearStalePrimary(product, imageUrls))
            {
                await SaveAsync(cancellationToken);
            }

            SyncImages(product, imageUrls);
            await SaveAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _auditLogService.LogAsync(adminUserId, "STORE_PRODUCT_UPDATED", nameof(Product), id, ipAddress, userAgent,
            oldValue, Snapshot(product), cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateStoreStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var updated = await _unitOfWork.Repository<Product>().GetQueryable(asNoTracking: false)
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsActive, request.IsActive)
                .SetProperty(p => p.UpdatedAt, UtcNow)
                .SetProperty(p => p.UpdatedUser, adminUserId), cancellationToken);
        if (updated == 0)
        {
            throw new NotFoundException("Urun", id);
        }

        await _auditLogService.LogAsync(adminUserId, "STORE_PRODUCT_STATUS_CHANGED", nameof(Product), id, ipAddress, userAgent,
            newValue: new { request.IsActive }, cancellationToken: cancellationToken);
    }

    public async Task<AdminProductDto> AdjustStockAsync(long id, AdminAdjustStockRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        if (request.Delta == 0)
        {
            throw new AppValidationException(nameof(request.Delta), "Stok degisimi 0 olamaz.");
        }

        var delta = request.Delta;
        var updated = await _unitOfWork.Repository<Product>().GetQueryable(asNoTracking: false)
            .Where(p => p.Id == id && p.StockQuantity + delta >= 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.StockQuantity, p => p.StockQuantity + delta)
                .SetProperty(p => p.UpdatedAt, UtcNow)
                .SetProperty(p => p.UpdatedUser, adminUserId), cancellationToken);

        if (updated == 0)
        {
            if (!await _unitOfWork.Repository<Product>().GetQueryable().AnyAsync(p => p.Id == id, cancellationToken))
            {
                throw new NotFoundException("Urun", id);
            }

            throw new ConflictException("Stok 0'in altina dusurulemez. Guncel stogu kontrol edin.");
        }

        var result = await GetByIdAsync(id, cancellationToken);
        await _auditLogService.LogAsync(adminUserId, "STORE_PRODUCT_STOCK_ADJUSTED", nameof(Product), id, ipAddress, userAgent,
            newValue: new { request.Delta, request.Note, result.StockQuantity }, cancellationToken: cancellationToken);
        return result;
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Urun", id);

        if (await _unitOfWork.Repository<StoreOrderItem>().GetQueryable().AnyAsync(i => i.ProductId == id, cancellationToken))
        {
            throw new ConflictException("Bu urun siparislerde yer aldigi icin silinemez. Satistan kaldirmak icin pasife alin.");
        }

        var oldValue = Snapshot(product);
        _unitOfWork.Repository<Product>().Remove(product);
        await SaveAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "STORE_PRODUCT_DELETED", nameof(Product), id, ipAddress, userAgent,
            oldValue, cancellationToken: cancellationToken);
    }

    /// <summary>Alanlari dogrulayip urune uygular; dogrulanmis gorsel URL'lerini (sirasiyla) dondurur.</summary>
    private async Task<List<string>> ApplyAsync(Product product, AdminUpsertProductRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length < 2) throw new AppValidationException(nameof(request.Name), "Urun adi zorunludur.");

        var description = request.Description?.Trim() ?? string.Empty;
        if (description.Length < 10) throw new AppValidationException(nameof(request.Description), "Aciklama en az 10 karakter olmalidir.");

        var brand = request.Brand?.Trim() ?? string.Empty;
        if (brand.Length == 0) throw new AppValidationException(nameof(request.Brand), "Marka zorunludur.");

        var sku = request.Sku?.Trim().ToUpperInvariant() ?? string.Empty;
        if (sku.Length < 2) throw new AppValidationException(nameof(request.Sku), "SKU zorunludur.");

        var price = decimal.Round(request.Price, 2, MidpointRounding.AwayFromZero);
        if (price <= 0) throw new AppValidationException(nameof(request.Price), "Fiyat 0'dan buyuk olmalidir.");

        decimal? discountPrice = request.DiscountPrice.HasValue ? decimal.Round(request.DiscountPrice.Value, 2, MidpointRounding.AwayFromZero) : null;
        if (discountPrice.HasValue && (discountPrice.Value <= 0 || discountPrice.Value >= price))
        {
            throw new AppValidationException(nameof(request.DiscountPrice), "Indirimli fiyat 0'dan buyuk ve normal fiyattan dusuk olmalidir.");
        }

        if (!await _unitOfWork.Repository<StoreCategory>().GetQueryable().AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            throw new AppValidationException(nameof(request.CategoryId), "Kategori bulunamadi.");
        }

        var features = request.Features.Select(f => f?.Trim() ?? string.Empty).Where(f => f.Length > 0).ToList();
        if (features.Any(f => f.Length > 200)) throw new AppValidationException(nameof(request.Features), "Her ozellik en fazla 200 karakter olabilir.");

        var specs = request.Specs
            .Select(s => new AdminProductSpecDto { Label = s.Label?.Trim() ?? string.Empty, Value = s.Value?.Trim() ?? string.Empty })
            .Where(s => s.Label.Length > 0 || s.Value.Length > 0)
            .ToList();
        if (specs.Any(s => s.Label.Length == 0 || s.Value.Length == 0))
        {
            throw new AppValidationException(nameof(request.Specs), "Teknik ozelliklerin adi ve degeri birlikte girilmelidir.");
        }

        var variants = ValidateVariants(request.Variants, price, discountPrice);
        var imageUrls = request.ImageUrls
            .Select((url, index) => AdminStoreInput.HttpsUrl(url, $"{nameof(request.ImageUrls)}[{index}]"))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (imageUrls.Count > 10) throw new AppValidationException(nameof(request.ImageUrls), "En fazla 10 gorsel eklenebilir.");

        product.Name = name;
        product.Slug = AdminStoreInput.Slug(request.Slug, name, 220, nameof(request.Slug));
        product.Description = description;
        product.Sku = sku;
        product.CategoryId = request.CategoryId;
        product.Brand = brand;
        product.Price = price;
        product.DiscountPrice = discountPrice;
        product.IsActive = request.IsActive;
        product.IsFeatured = request.IsFeatured;
        product.SortOrder = request.SortOrder;
        product.FeaturesJson = features.Count == 0 ? null : JsonSerializer.Serialize(features, AdminStoreInput.Json);
        product.SpecsJson = specs.Count == 0 ? null : JsonSerializer.Serialize(specs, AdminStoreInput.Json);
        product.VariantsJson = variants.Count == 0 ? null : JsonSerializer.Serialize(variants, AdminStoreInput.Json);
        return imageUrls;
    }

    /// <summary>Ana gorsel olmaya devam etmeyecek mevcut ana gorselin bayragini kapatir; degisiklik olduysa true.</summary>
    private bool ClearStalePrimary(Product product, List<string> urls)
    {
        var changed = false;
        foreach (var image in product.Images.Where(i => i.IsPrimary && (urls.Count == 0 || i.ImageUrl != urls[0])))
        {
            image.IsPrimary = false;
            image.UpdatedAt = UtcNow;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Gorsel listesini verilen sirayla esitler: ayni URL'deki kayit korunur (yalnizca sira/ana gorsel guncellenir),
    /// listede olmayanlar silinir, yeniler eklenir. Ilk gorsel ana gorseldir.
    /// </summary>
    private void SyncImages(Product product, List<string> urls)
    {
        var repo = _unitOfWork.Repository<ProductImage>();
        var existing = product.Images.ToList();
        foreach (var image in existing.Where(i => !urls.Contains(i.ImageUrl)))
        {
            product.Images.Remove(image);
            repo.Remove(image);
        }

        var now = UtcNow;
        for (var index = 0; index < urls.Count; index++)
        {
            var image = product.Images.FirstOrDefault(i => i.ImageUrl == urls[index]);
            if (image is null)
            {
                product.Images.Add(new ProductImage { ImageUrl = urls[index], SortOrder = index, IsPrimary = index == 0, CreatedAt = now });
                continue;
            }

            if (image.SortOrder != index || image.IsPrimary != (index == 0))
            {
                image.SortOrder = index;
                image.IsPrimary = index == 0;
                image.UpdatedAt = now;
            }
        }
    }

    private static List<AdminProductVariantGroupDto> ValidateVariants(List<AdminProductVariantGroupDto> groups, decimal price, decimal? discountPrice)
    {
        const string field = nameof(AdminUpsertProductRequest.Variants);
        var result = new List<AdminProductVariantGroupDto>();
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        var lowestPrice = discountPrice ?? price;

        foreach (var group in groups)
        {
            var id = group.Id?.Trim().ToLowerInvariant() ?? string.Empty;
            var label = group.Label?.Trim() ?? string.Empty;
            var kind = string.IsNullOrWhiteSpace(group.Kind) ? "option" : group.Kind.Trim().ToLowerInvariant();
            if (!VariantIdPattern().IsMatch(id)) throw new AppValidationException(field, "Varyant grubu kimligi kucuk harf, rakam, - ve _ icermelidir (orn. beden).");
            if (!groupIds.Add(id)) throw new AppValidationException(field, $"'{id}' varyant grubu birden fazla kez tanimlanmis.");
            if (label.Length == 0) throw new AppValidationException(field, "Varyant grubu adi zorunludur.");
            if (!VariantKinds.Contains(kind)) throw new AppValidationException(field, "Varyant turu size, color, package veya option olmalidir.");
            if (group.Options.Count == 0 || group.Options.Count > 30) throw new AppValidationException(field, $"'{label}' grubunda 1-30 secenek olmalidir.");

            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            var options = new List<AdminProductVariantOptionDto>();
            foreach (var option in group.Options)
            {
                var optionId = option.Id?.Trim().ToLowerInvariant() ?? string.Empty;
                var optionLabel = option.Label?.Trim() ?? string.Empty;
                if (!VariantIdPattern().IsMatch(optionId)) throw new AppValidationException(field, "Secenek kimligi kucuk harf, rakam, - ve _ icermelidir (orn. xl).");
                if (!optionIds.Add(optionId)) throw new AppValidationException(field, $"'{label}' grubunda '{optionId}' secenegi tekrar ediyor.");
                if (optionLabel.Length == 0) throw new AppValidationException(field, "Secenek adi zorunludur.");

                var delta = decimal.Round(option.PriceDelta, 2, MidpointRounding.AwayFromZero);
                if (lowestPrice + delta <= 0)
                {
                    throw new AppValidationException(field, $"'{optionLabel}' seceneginin fiyat farki urun fiyatini 0'a veya altina dusuruyor.");
                }

                var swatch = AdminStoreInput.NullIfBlank(option.Swatch);
                if (swatch is not null && !SwatchPattern().IsMatch(swatch)) throw new AppValidationException(field, "Renk kodu #RRGGBB formatinda olmalidir.");

                options.Add(new AdminProductVariantOptionDto { Id = optionId, Label = optionLabel, PriceDelta = delta, InStock = option.InStock, Swatch = swatch });
            }

            if (options.All(o => !o.InStock))
            {
                throw new AppValidationException(field, $"'{label}' grubunda en az bir secenek satista olmalidir; urunu satistan kaldirmak icin pasife alin.");
            }

            result.Add(new AdminProductVariantGroupDto { Id = id, Label = label, Kind = kind, Options = options });
        }

        return result;
    }

    private Task SaveAsync(CancellationToken cancellationToken) =>
        AdminStoreInput.SaveAsync(_unitOfWork, constraint => constraint switch
        {
            "IX_Product_Sku" => "Bu SKU baska bir urunde kullaniliyor.",
            "IX_Product_Slug" => "Bu slug baska bir urunde kullaniliyor.",
            _ => "Urun bilgileri baska bir kayitla cakisiyor (SKU veya slug).",
        }, cancellationToken);

    private async Task<List<AdminProductListItemDto>> ToListItemsAsync(List<Product> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var categoryIds = rows.Select(r => r.CategoryId).Distinct().ToList();
        var categories = await _unitOfWork.Repository<StoreCategory>().GetQueryable()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var images = await _unitOfWork.Repository<ProductImage>().GetQueryable()
            .Where(i => ids.Contains(i.ProductId))
            .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new { i.ProductId, i.ImageUrl })
            .ToListAsync(cancellationToken);
        var primary = images.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.First().ImageUrl);

        return rows.Select(p => new AdminProductListItemDto
        {
            Id = p.Id,
            Name = p.Name,
            Slug = p.Slug,
            Sku = p.Sku,
            Brand = p.Brand,
            CategoryId = p.CategoryId,
            CategoryName = categories.GetValueOrDefault(p.CategoryId) ?? string.Empty,
            Price = p.Price,
            DiscountPrice = p.DiscountPrice,
            StockQuantity = p.StockQuantity,
            IsActive = p.IsActive,
            IsFeatured = p.IsFeatured,
            SortOrder = p.SortOrder,
            ImageUrl = primary.GetValueOrDefault(p.Id),
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
        }).ToList();
    }

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, AdminStoreInput.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object Snapshot(Product p) => new
    {
        p.Name, p.Slug, p.Sku, p.CategoryId, p.Brand, p.Price, p.DiscountPrice, p.StockQuantity, p.IsActive, p.IsFeatured, p.SortOrder,
        Images = p.Images.Count,
    };
}
