using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MerfitApi.Business.Common;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin.Store;

/// <summary>MB Shop admin servislerinin ortak girdi dogrulama ve kayit yardimcilari.</summary>
internal static partial class AdminStoreInput
{
    /// <summary>
    /// Urun JSON alanlari (FeaturesJson, SpecsJson, VariantsJson) MerfitCustomerApi'deki StoreJson ile ayni bicimde
    /// (camelCase) yazilir; mobil API bu alanlari oradan okur.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    public static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Verilen slug'i dogrular; bos ise metinden Turkce karakterleri sadelestirerek uretir.</summary>
    public static string Slug(string? slug, string fallbackText, int maxLength, string field)
    {
        var explicitSlug = NullIfBlank(slug);
        if (explicitSlug is not null)
        {
            var lowered = explicitSlug.ToLowerInvariant();
            if (lowered.Length > maxLength || !SlugPattern().IsMatch(lowered))
            {
                throw new AppValidationException(field, "Slug yalnizca kucuk harf, rakam ve tek tire icerebilir (orn. kas-gelistirme).");
            }

            return lowered;
        }

        var generated = Slugify(fallbackText);
        if (generated.Length == 0)
        {
            throw new AppValidationException(field, "Addan slug uretilemedi; lutfen slug girin.");
        }

        return generated.Length > maxLength ? generated[..maxLength].TrimEnd('-') : generated;
    }

    public static string Slugify(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lastDash = true;
        foreach (var raw in text.Trim().ToLower(Turkish))
        {
            var c = raw switch
            {
                'ç' => 'c', 'ğ' => 'g', 'ı' => 'i', 'ö' => 'o', 'ş' => 's', 'ü' => 'u', 'â' => 'a', 'î' => 'i', 'û' => 'u',
                _ => raw,
            };
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
                lastDash = false;
            }
            else if (!lastDash)
            {
                builder.Append('-');
                lastDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>Gorsel adresleri yalnizca https olabilir (mobil uygulama http adresleri yuklemez).</summary>
    public static string? HttpsUrl(string? value, string field)
    {
        var trimmed = NullIfBlank(value);
        if (trimmed is null) return null;

        if (trimmed.Length > 500 || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new AppValidationException(field, "Gorsel adresi https:// ile baslayan gecerli bir URL olmalidir.");
        }

        return trimmed;
    }

    public static DateTime? ToUtc(DateTime? value) => value?.Kind switch
    {
        null => null,
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value!.Value, DateTimeKind.Utc),
    };

    public static TEnum ParseEnum<TEnum>(string? value, string field, string message) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed)
            || int.TryParse(value.Trim(), out _))
        {
            throw new AppValidationException(field, message);
        }

        return parsed;
    }

    /// <summary>
    /// Degisiklikleri kaydeder; benzersizlik (slug/SKU) ihlalini ve eszamanli guncellemeyi 409'a cevirir.
    /// </summary>
    public static async Task SaveAsync(IUnitOfWork unitOfWork, Func<string?, string> uniqueMessage, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            unitOfWork.ClearTracking();
            throw new ConflictException("Kayit ayni anda baska bir islemle degisti. Sayfayi yenileyip tekrar deneyin.");
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out var constraint))
        {
            unitOfWork.ClearTracking();
            throw new ConflictException(uniqueMessage(constraint));
        }
    }
}
