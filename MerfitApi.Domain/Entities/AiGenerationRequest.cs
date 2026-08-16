using MerfitApi.Domain.Entities.Enums;

namespace MerfitApi.Domain.Entities;

/// <summary>
/// AiGenerationRequest varligini temsil eder.
/// </summary>
public class AiGenerationRequest
{
    /// <summary>
    /// Yapay zeka uretim istegi kaydinin benzersiz kimligi.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Istegi olusturan kullanicinin kimligi.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Istegin turu.
    /// </summary>
    public AiRequestType Type { get; set; }

    /// <summary>
    /// Yapay zekaya gonderilen istem (prompt) metni.
    /// </summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// Istegin guncel durumu.
    /// </summary>
    public AiGenerationStatus Status { get; set; }

    /// <summary>
    /// Istek icin kullanilan yapay zeka modeli.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Uretimin baslama tarihi.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Uretimin tamamlanma tarihi.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Kaydin olusturulma tarihi.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
