using System.ComponentModel.DataAnnotations;
using MerfitApi.Business.Common.Pagination;

namespace MerfitApi.Business.Dtos.Admin.Content;

/// <summary>
/// Mobil ana sayfadaki "Kampanyalar" bolumunde gosterilen kampanya. AppContent tablosunda Type = "Campaign"
/// olarak saklanir; Description kisa ozet, Body detay metnidir.
/// </summary>
public class AdminCampaignDto
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Body { get; set; }
    public string? ImageUrl { get; set; }
    public string? LinkUrl { get; set; }
    public string? Category { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    /// <summary>"Live" | "Scheduled" | "Ended" | "Passive" (IsActive ve tarih araligina gore).</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminCampaignListRequest : PagedRequest
{
    public bool? IsActive { get; set; }

    /// <summary>"Live" | "Scheduled" | "Ended" | "Passive"; bos ise hepsi.</summary>
    public string? Status { get; set; }
}

public class AdminUpsertCampaignRequest
{
    [Required(ErrorMessage = "Baslik zorunludur.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Baslik 2-200 karakter olmalidir.")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Kartta gorunen kisa aciklama.</summary>
    [StringLength(300, ErrorMessage = "Kisa aciklama en fazla 300 karakter olabilir.")]
    public string? Summary { get; set; }

    /// <summary>Detay ekranindaki metin.</summary>
    [StringLength(8000, ErrorMessage = "Detay metni en fazla 8000 karakter olabilir.")]
    public string? Body { get; set; }

    [StringLength(500, ErrorMessage = "Gorsel URL'i en fazla 500 karakter olabilir.")]
    public string? ImageUrl { get; set; }

    [StringLength(500, ErrorMessage = "Baglanti en fazla 500 karakter olabilir.")]
    public string? LinkUrl { get; set; }

    [StringLength(100, ErrorMessage = "Kategori en fazla 100 karakter olabilir.")]
    public string? Category { get; set; }

    [Range(0, 10000, ErrorMessage = "Sira 0-10000 arasinda olmalidir.")]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? StartAt { get; set; }

    public DateTime? EndAt { get; set; }
}

public class AdminReorderCampaignsRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "En az bir kampanya gonderilmelidir.")]
    [MaxLength(200, ErrorMessage = "Tek seferde en fazla 200 kampanya siralanabilir.")]
    public List<AdminCampaignOrderItem> Items { get; set; } = new();
}

public class AdminCampaignOrderItem
{
    [Range(1, long.MaxValue)]
    public long Id { get; set; }

    [Range(0, 10000, ErrorMessage = "Sira 0-10000 arasinda olmalidir.")]
    public int SortOrder { get; set; }
}
