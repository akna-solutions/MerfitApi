using MerfitApi.Domain.Exceptions;

namespace MerfitApi.Business.Services.Admin.Store;

/// <summary>
/// Admin'in elle atayabildigi urun rozetleri. CustomerApi'deki StoreBadgeCodes.Assignable ve veritabanindaki
/// CK_Product_HighlightBadge_Valid kisitiyla senkron tutun.
/// </summary>
public static class AdminProductBadges
{
    public const string Advantageous = "advantageous";
    public const string MbfitPick = "mbfit_pick";

    public static readonly IReadOnlyList<string> Assignable = new[] { Advantageous, MbfitPick };

    /// <summary>Bos degeri null'a cevirir; bilinmeyen kodu reddeder.</summary>
    public static string? Normalize(string? value, string field)
    {
        var code = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code)) return null;
        if (!Assignable.Contains(code)) throw new AppValidationException(field, "Gecersiz urun rozeti.");
        return code;
    }
}
