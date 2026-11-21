using System.ComponentModel.DataAnnotations;
using MerfitApi.Business.Common.Pagination;

namespace MerfitApi.Business.Dtos.Admin.Store;

// MB Shop urun yorumlari yonetimi. Tablolar MerfitCustomerApi'nin "store" semasindadir. Durumlar enum adlariyla doner
// ("Published", "Hidden", "Deleted"; rapor: "Open", "Resolved", "Dismissed").

/// <summary>
/// GET /api/admin/store/reviews. Search: yorum/baslik, urun adi, kullanici e-postasi veya kullanici adi.
/// SortBy: createdAt (varsayilan) | rating | helpful | reports; SortDirection: asc | desc (varsayilan desc).
/// </summary>
public class AdminProductReviewListRequest : PagedRequest
{
    public long? ProductId { get; set; }
    public long? UserId { get; set; }
    public int? Rating { get; set; }

    /// <summary>"Published" | "Hidden" | "Deleted"; bos ise silinmemis tum yorumlar.</summary>
    public string? Status { get; set; }

    /// <summary>true: yalnizca acik raporu olan yorumlar.</summary>
    public bool ReportedOnly { get; set; }

    public bool? VerifiedPurchase { get; set; }
}

public class AdminProductReviewListItemDto
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? UserName { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
    public bool IsVerifiedPurchase { get; set; }
    public string Status { get; set; } = string.Empty;
    public int HelpfulCount { get; set; }
    public int NotHelpfulCount { get; set; }
    public int ImageCount { get; set; }
    public int OpenReportCount { get; set; }
    public int ReportCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
}

public class AdminProductReviewImageDto
{
    public long Id { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public int ByteSize { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class AdminProductReviewReportDto
{
    public long Id { get; set; }
    public long ReporterUserId { get; set; }
    public string? ReporterEmail { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class AdminProductReviewHistoryDto
{
    public string Action { get; set; } = string.Empty;
    public long? AdminUserId { get; set; }
    public string? AdminEmail { get; set; }
    public string? Note { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminProductReviewDto : AdminProductReviewListItemDto
{
    public string? ModerationNote { get; set; }
    public DateTime? ModeratedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<AdminProductReviewImageDto> Images { get; set; } = new();
    public List<AdminProductReviewReportDto> Reports { get; set; } = new();

    /// <summary>Kullanici olaylari (olusturma, duzenleme, silme) ve admin islemleri (audit log), eskiden yeniye.</summary>
    public List<AdminProductReviewHistoryDto> History { get; set; } = new();

    /// <summary>Bu yorumda yapilabilecek admin durum gecisleri.</summary>
    public List<string> AllowedStatusTransitions { get; set; } = new();
}

public class AdminUpdateProductReviewStatusRequest
{
    /// <summary>"Published" (yeniden yayinla) veya "Hidden" (gizle).</summary>
    [Required(ErrorMessage = "Yeni durum zorunludur.")]
    public string Status { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Not en fazla 500 karakter olabilir.")]
    public string? Note { get; set; }
}

public class AdminProductReviewNoteRequest
{
    [StringLength(500, ErrorMessage = "Not en fazla 500 karakter olabilir.")]
    public string? Note { get; set; }
}

public sealed record AdminProductReviewImageFile(byte[] Content, string ContentType);
