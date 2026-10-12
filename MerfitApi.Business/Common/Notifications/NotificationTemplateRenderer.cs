using System.Text.RegularExpressions;

namespace MerfitApi.Business.Common.Notifications;

/// <summary>
/// Bildirim sablonlarindaki {placeholder} alanlarini doldurur. Desteklenen alanlar
/// MerfitCustomerApi'deki gonderim motoru (NotificationTemplateRenderer) ile aynidir; iki repoda birlikte guncellenmelidir.
/// Degeri olmayan alanlar bos metne cevrilir ve fazladan bosluklar temizlenir.
/// </summary>
public static partial class NotificationTemplateRenderer
{
    public const string FirstName = "firstName";
    public const string MealName = "mealName";
    public const string WorkoutName = "workoutName";
    public const string RewardTitle = "rewardTitle";
    public const string Rank = "rank";
    public const string PeriodLabel = "periodLabel";

    public static readonly IReadOnlySet<string> SupportedPlaceholders =
        new HashSet<string>(StringComparer.Ordinal) { FirstName, MealName, WorkoutName, RewardTitle, Rank, PeriodLabel };

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultiSpaceRegex();

    public static string Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        var rendered = PlaceholderRegex().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value ?? string.Empty : string.Empty);
        rendered = MultiSpaceRegex().Replace(rendered, " ");
        return rendered.Replace(" ,", ",").Replace(" !", "!").Replace(" .", ".").Trim();
    }

    /// <summary>Sablonda gecen alan adlari.</summary>
    public static IReadOnlyList<string> FindPlaceholders(string template) =>
        PlaceholderRegex().Matches(template).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>Sablonda gecen ama desteklenmeyen alan adlari.</summary>
    public static IReadOnlyList<string> FindUnsupportedPlaceholders(string template) =>
        PlaceholderRegex().Matches(template)
            .Select(m => m.Groups[1].Value)
            .Where(name => !SupportedPlaceholders.Contains(name))
            .Distinct()
            .ToList();

    public static string MealDisplayName(MerfitApi.Domain.Entities.Enums.MealType mealType) => mealType switch
    {
        MerfitApi.Domain.Entities.Enums.MealType.Breakfast => "kahvaltı",
        MerfitApi.Domain.Entities.Enums.MealType.Lunch => "öğle yemeği",
        MerfitApi.Domain.Entities.Enums.MealType.Dinner => "akşam yemeği",
        _ => "ara öğün",
    };
}
