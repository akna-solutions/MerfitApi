using System.ComponentModel.DataAnnotations;
using MerfitApi.Business.Common.Pagination;

namespace MerfitApi.Business.Dtos.Admin.Store;

// MB Shop yonetimi. Tablolar MerfitCustomerApi'nin "store" semasindadir (migration orada); mobil uygulama ayni
// kayitlari /api/store uc noktalarindan okur. Durum alanlari enum adlariyla (orn. "Received", "Paid") doner.

public class AdminStoreCategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? Icon { get; set; }
    public long? ParentCategoryId { get; set; }
    public string? ParentCategoryName { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int ProductCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminStoreCategoryListRequest : PagedRequest
{
    public bool? IsActive { get; set; }
}

public class AdminUpsertStoreCategoryRequest
{
    [Required(ErrorMessage = "Kategori adi zorunludur.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Kategori adi 2-120 karakter olmalidir.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Bos ise addan uretilir. Yalnizca kucuk harf, rakam ve tire.</summary>
    [StringLength(140, ErrorMessage = "Slug en fazla 140 karakter olabilir.")]
    public string? Slug { get; set; }

    [StringLength(1000, ErrorMessage = "Aciklama en fazla 1000 karakter olabilir.")]
    public string? Description { get; set; }

    [StringLength(500, ErrorMessage = "Gorsel URL'i en fazla 500 karakter olabilir.")]
    public string? ImageUrl { get; set; }

    /// <summary>Mobil uygulamadaki ikon adi (Ionicons, orn. "barbell-outline").</summary>
    [StringLength(60, ErrorMessage = "Ikon adi en fazla 60 karakter olabilir.")]
    public string? Icon { get; set; }

    public long? ParentCategoryId { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, 10000, ErrorMessage = "Sira 0-10000 arasinda olmalidir.")]
    public int SortOrder { get; set; }
}

public class AdminUpdateStoreStatusRequest
{
    public bool IsActive { get; set; }
}

public class AdminProductVariantOptionDto
{
    [Required, StringLength(40)]
    public string Id { get; set; } = string.Empty;

    [Required, StringLength(60)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Birim fiyata eklenen tutar (TL); negatif olabilir ama fiyati sifira/altina dusuremez.</summary>
    [Range(-100000, 100000)]
    public decimal PriceDelta { get; set; }

    public bool InStock { get; set; } = true;

    /// <summary>Renk secenegi icin #RRGGBB.</summary>
    [StringLength(7)]
    public string? Swatch { get; set; }
}

public class AdminProductVariantGroupDto
{
    [Required, StringLength(40)]
    public string Id { get; set; } = string.Empty;

    [Required, StringLength(60)]
    public string Label { get; set; } = string.Empty;

    /// <summary>"size" | "color" | "package" | "option".</summary>
    [StringLength(20)]
    public string Kind { get; set; } = "option";

    public List<AdminProductVariantOptionDto> Options { get; set; } = new();
}

public class AdminProductSpecDto
{
    [Required, StringLength(80)]
    public string Label { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Value { get; set; } = string.Empty;
}

public class AdminProductImageDto
{
    public long Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
}

public class AdminProductListItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminProductDto : AdminProductListItemDto
{
    public string Description { get; set; } = string.Empty;
    public List<string> Features { get; set; } = new();
    public List<AdminProductSpecDto> Specs { get; set; } = new();
    public List<AdminProductVariantGroupDto> Variants { get; set; } = new();
    public List<AdminProductImageDto> Images { get; set; } = new();
}

public class AdminProductListRequest : PagedRequest
{
    public long? CategoryId { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsFeatured { get; set; }

    /// <summary>true: stok 0; false: stokta olanlar.</summary>
    public bool? OutOfStock { get; set; }

    /// <summary>Bu degerin altinda (dahil) stoklu urunler.</summary>
    [Range(0, 1000000)]
    public int? LowStockThreshold { get; set; }
}

public class AdminUpsertProductRequest
{
    [Required(ErrorMessage = "Urun adi zorunludur.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Urun adi 2-200 karakter olmalidir.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(220, ErrorMessage = "Slug en fazla 220 karakter olabilir.")]
    public string? Slug { get; set; }

    [Required(ErrorMessage = "Aciklama zorunludur.")]
    [StringLength(4000, MinimumLength = 10, ErrorMessage = "Aciklama 10-4000 karakter olmalidir.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "SKU zorunludur.")]
    [StringLength(64, MinimumLength = 2, ErrorMessage = "SKU 2-64 karakter olmalidir.")]
    [RegularExpression("^[A-Za-z0-9._-]+$", ErrorMessage = "SKU yalnizca harf, rakam, nokta, alt cizgi ve tire icerebilir.")]
    public string Sku { get; set; } = string.Empty;

    [Range(1, long.MaxValue, ErrorMessage = "Kategori secilmelidir.")]
    public long CategoryId { get; set; }

    [Required(ErrorMessage = "Marka zorunludur.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Marka en fazla 100 karakter olabilir.")]
    public string Brand { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Fiyat 0,01-1.000.000 TL arasinda olmalidir.")]
    public decimal Price { get; set; }

    /// <summary>Indirimli satis fiyati; fiyattan dusuk olmalidir. Bos ise indirim yoktur.</summary>
    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Indirimli fiyat 0,01-1.000.000 TL arasinda olmalidir.")]
    public decimal? DiscountPrice { get; set; }

    /// <summary>Yalnizca olusturmada kullanilir. Mevcut urunun stogu PATCH /stock ile (satislarla yarismadan) degistirilir.</summary>
    [Range(0, 1000000, ErrorMessage = "Stok 0-1.000.000 arasinda olmalidir.")]
    public int StockQuantity { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }

    [Range(0, 10000, ErrorMessage = "Sira 0-10000 arasinda olmalidir.")]
    public int SortOrder { get; set; }

    [MaxLength(20, ErrorMessage = "En fazla 20 ozellik eklenebilir.")]
    public List<string> Features { get; set; } = new();

    [MaxLength(30, ErrorMessage = "En fazla 30 teknik ozellik eklenebilir.")]
    public List<AdminProductSpecDto> Specs { get; set; } = new();

    [MaxLength(5, ErrorMessage = "En fazla 5 varyant grubu eklenebilir.")]
    public List<AdminProductVariantGroupDto> Variants { get; set; } = new();

    /// <summary>Gorsel URL'leri (https). Ilk sirada olan ana gorseldir. Kayitli liste bununla degistirilir.</summary>
    [MaxLength(10, ErrorMessage = "En fazla 10 gorsel eklenebilir.")]
    public List<string> ImageUrls { get; set; } = new();
}

public class AdminAdjustStockRequest
{
    /// <summary>Stoga eklenecek (pozitif) veya dusulecek (negatif) adet. Stok 0'in altina dusurulemez.</summary>
    [Range(-1000000, 1000000, ErrorMessage = "Degisim -1.000.000 ile 1.000.000 arasinda olmalidir.")]
    public int Delta { get; set; }

    [StringLength(200)]
    public string? Note { get; set; }
}

public class AdminStoreCampaignDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? Tint { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    /// <summary>"Live" | "Scheduled" | "Ended" | "Passive".</summary>
    public string Status { get; set; } = string.Empty;

    public List<long> ProductIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminStoreCampaignListRequest : PagedRequest
{
    public bool? IsActive { get; set; }
}

public class AdminUpsertStoreCampaignRequest
{
    [Required(ErrorMessage = "Baslik zorunludur.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Baslik 2-200 karakter olmalidir.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Aciklama en fazla 500 karakter olabilir.")]
    public string? Description { get; set; }

    [StringLength(500, ErrorMessage = "Gorsel URL'i en fazla 500 karakter olabilir.")]
    public string? ImageUrl { get; set; }

    /// <summary>Kart rengi (#RRGGBB).</summary>
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Renk #RRGGBB formatinda olmalidir.")]
    public string? Tint { get; set; }

    [Range(0, 10000, ErrorMessage = "Sira 0-10000 arasinda olmalidir.")]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    [MaxLength(200, ErrorMessage = "Bir kampanyaya en fazla 200 urun eklenebilir.")]
    public List<long> ProductIds { get; set; } = new();
}

public class AdminStoreOrderListItemDto
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? RecipientName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "TRY";
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminStoreOrderAddressDto
{
    public string Title { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
}

public class AdminStoreOrderItemDto
{
    public long Id { get; set; }
    public long? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? VariantLabel { get; set; }
    public decimal UnitListPrice { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class AdminStoreOrderHistoryDto
{
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminStoreOrderDto : AdminStoreOrderListItemDto
{
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public string? PaymentProvider { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public AdminStoreOrderAddressDto ShippingAddress { get; set; } = new();
    public List<AdminStoreOrderItemDto> Items { get; set; } = new();
    public List<AdminStoreOrderHistoryDto> History { get; set; } = new();

    /// <summary>Bu siparis icin gecerli bir sonraki siparis durumlari (admin arayuzu butonlari icin).</summary>
    public List<string> AllowedStatusTransitions { get; set; } = new();

    /// <summary>Bu siparis icin gecerli bir sonraki odeme durumlari.</summary>
    public List<string> AllowedPaymentTransitions { get; set; } = new();
}

public class AdminStoreOrderListRequest : PagedRequest
{
    /// <summary>"Received" | "Preparing" | "Shipped" | "Delivered" | "Cancelled".</summary>
    public string? Status { get; set; }

    /// <summary>"Pending" | "Paid" | "Failed" | "RefundPending" | "Refunded" | "Cancelled".</summary>
    public string? PaymentStatus { get; set; }

    /// <summary>"CashOnDelivery" | "BankTransfer" | "Card".</summary>
    public string? PaymentMethod { get; set; }

    public long? UserId { get; set; }

    /// <summary>Siparis tarihi alt siniri (UTC, dahil).</summary>
    public DateTime? From { get; set; }

    /// <summary>Siparis tarihi ust siniri (UTC, haric).</summary>
    public DateTime? To { get; set; }
}

public class AdminUpdateStoreOrderStatusRequest
{
    [Required(ErrorMessage = "Yeni durum zorunludur.")]
    public string Status { get; set; } = string.Empty;

    [StringLength(300, ErrorMessage = "Not en fazla 300 karakter olabilir.")]
    public string? Note { get; set; }
}

public class AdminUpdateStorePaymentStatusRequest
{
    [Required(ErrorMessage = "Yeni odeme durumu zorunludur.")]
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary>Havale dekont no / saglayici islem no gibi referans.</summary>
    [StringLength(120, ErrorMessage = "Referans en fazla 120 karakter olabilir.")]
    public string? PaymentReference { get; set; }

    [StringLength(300, ErrorMessage = "Not en fazla 300 karakter olabilir.")]
    public string? Note { get; set; }
}
