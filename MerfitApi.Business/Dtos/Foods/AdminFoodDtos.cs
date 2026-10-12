using System.ComponentModel.DataAnnotations;
using MerfitApi.Business.Common.Pagination;

namespace MerfitApi.Business.Dtos.Admin.Foods;

public class AdminFoodListItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? Brand { get; set; }
    public decimal ServingSize { get; set; }
    public string ServingUnit { get; set; } = string.Empty;
    public decimal Calories { get; set; }
    public decimal Protein { get; set; }
    public decimal Carbs { get; set; }
    public decimal Fat { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Besinsel kategori ("ProteinSource", "Dairy", "Legume", "GrainStarch", "Vegetable", "Fruit", "NutsSeedsFats", "Other"); null = siniflandirilmamis.</summary>
    public string? Category { get; set; }

    /// <summary>Uygun ogunler ("Breakfast", "Lunch", "Dinner", "Snack"); null = siniflandirilmamis.</summary>
    public List<string>? MealTypes { get; set; }

    /// <summary>Icerdigi alerjenler; null = dogrulanmamis (alerjisi olan kullanicilara onerilmez), bos liste = alerjen yok.</summary>
    public List<string>? Allergens { get; set; }

    /// <summary>Uygun diyetler ("Vegetarian", "Vegan"); null = dogrulanmamis (vejetaryen/vegan kullanicilara onerilmez).</summary>
    public List<string>? DietTags { get; set; }

    /// <summary>Kisisel beslenme programinda kullanilabilir mi (kategori ve uygun ogun girilmis).</summary>
    public bool IsClassified { get; set; }
}

public class AdminFoodDetailDto : AdminFoodListItemDto
{
    public decimal? Fiber { get; set; }
    public decimal? Sugar { get; set; }
    public decimal? Sodium { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminFoodListRequest : PagedRequest
{
    public string? Barcode { get; set; }
    public string? Brand { get; set; }

    /// <summary>true: yalnizca siniflandirilmis, false: yalnizca siniflandirilmamis besinler.</summary>
    public bool? IsClassified { get; set; }
}

public class AdminUpsertFoodRequest
{
    [Required(ErrorMessage = "Isim alani zorunludur.")]
    [StringLength(300, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Barcode { get; set; }

    [StringLength(200)]
    public string? Brand { get; set; }

    [Range(0.01, 100000, ErrorMessage = "Porsiyon miktari 0'dan buyuk olmalidir.")]
    public decimal ServingSize { get; set; }

    [Required(ErrorMessage = "Porsiyon birimi zorunludur.")]
    [StringLength(20)]
    public string ServingUnit { get; set; } = string.Empty;

    [Range(0, 10000)]
    public decimal Calories { get; set; }

    [Range(0, 1000)]
    public decimal Protein { get; set; }

    [Range(0, 1000)]
    public decimal Carbs { get; set; }

    [Range(0, 1000)]
    public decimal Fat { get; set; }

    [Range(0, 1000)]
    public decimal? Fiber { get; set; }

    [Range(0, 1000)]
    public decimal? Sugar { get; set; }

    [Range(0, 100000)]
    public decimal? Sodium { get; set; }

    [StringLength(1000)]
    [Url(ErrorMessage = "Gecerli bir gorsel URL'i giriniz.")]
    public string? ImageUrl { get; set; }

    /// <summary>Besinsel kategori; bos birakilirsa besin siniflandirilmamis sayilir ve programlarda kullanilmaz.</summary>
    public string? Category { get; set; }

    /// <summary>Uygun ogunler; null/bos birakilirsa besin programlarda kullanilmaz.</summary>
    [MaxLength(4)]
    public List<string>? MealTypes { get; set; }

    /// <summary>Icerdigi alerjenler. null = dogrulanmamis; bos liste = alerjen icermedigi dogrulandi.</summary>
    [MaxLength(9)]
    public List<string>? Allergens { get; set; }

    /// <summary>Uygun diyetler. null = dogrulanmamis; bos liste = vejetaryen/vegan degil.</summary>
    [MaxLength(2)]
    public List<string>? DietTags { get; set; }
}
