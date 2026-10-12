using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;

namespace MerfitApi.Business.Common;

/// <summary>
/// Besin siniflandirma alanlarinin (kategori, uygun ogunler, alerjenler, diyet etiketleri) admin API'sindeki
/// metin karsiliklari. Degerler enum adlaridir ("ProteinSource", "Breakfast", "Gluten", "Vegan").
/// Bilinmeyen deger sessizce yok sayilmaz; alerjen gibi bir bilginin kaybolmasi kullaniciya uygun olmayan
/// besin onerilmesine yol acar.
/// </summary>
public static class FoodClassificationCodes
{
    public static string? CategoryToCode(FoodCategory? category) => category?.ToString();

    public static FoodCategory? ParseCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!TryParseSingle<FoodCategory>(value, out var category))
        {
            throw new AppValidationException("category", $"Gecersiz kategori: '{value}'. Gecerli degerler: {string.Join(", ", Enum.GetNames<FoodCategory>())}.");
        }

        return category;
    }

    /// <summary>Bayraklari ad listesine cevirir; null (dogrulanmamis) icin null doner.</summary>
    public static List<string>? FlagsToCodes<TEnum>(TEnum? flags) where TEnum : struct, Enum
    {
        if (!flags.HasValue)
        {
            return null;
        }

        var value = Convert.ToInt64(flags.Value);
        return Enum.GetValues<TEnum>()
            .Where(flag => Convert.ToInt64(flag) != 0 && (value & Convert.ToInt64(flag)) == Convert.ToInt64(flag))
            .Select(flag => flag.ToString())
            .ToList();
    }

    /// <summary>
    /// Ad listesini bayraklara cevirir. null liste null (dogrulanmamis/belirtilmemis) dondurur; bos liste
    /// "hicbiri" (0) anlamina gelir.
    /// </summary>
    public static TEnum? ParseFlags<TEnum>(IEnumerable<string>? values, string field) where TEnum : struct, Enum
    {
        if (values is null)
        {
            return null;
        }

        long combined = 0;
        foreach (var value in values)
        {
            if (!TryParseSingle<TEnum>(value, out var flag) || Convert.ToInt64(flag) == 0)
            {
                var valid = Enum.GetValues<TEnum>().Where(f => Convert.ToInt64(f) != 0).Select(f => f.ToString());
                throw new AppValidationException(field, $"Gecersiz deger: '{value}'. Gecerli degerler: {string.Join(", ", valid)}.");
            }

            combined |= Convert.ToInt64(flag);
        }

        return (TEnum)Enum.ToObject(typeof(TEnum), combined);
    }

    private static bool TryParseSingle<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && !long.TryParse(trimmed, out _)
            && !trimmed.Contains(',')
            && Enum.TryParse(trimmed, ignoreCase: true, out result)
            && Enum.IsDefined(result);
    }
}
