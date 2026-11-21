using System.ComponentModel.DataAnnotations;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Domain.Entities.Enums;

namespace MerfitApi.Business.Dtos.Admin.Exercises;

public class AdminExerciseListItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public long PrimaryMuscleGroupId { get; set; }
    public string PrimaryMuscleGroupName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminExerciseDetailDto : AdminExerciseListItemDto
{
    public string? Description { get; set; }
    public string? Instructions { get; set; }
    public string? EquipmentType { get; set; }
    public string? VideoUrl { get; set; }
    public decimal? CaloriesPerMinute { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Antrenman motoru metadata'si (bos alanlari motor tahmin eder ve raporlar).</summary>
    public AdminExerciseTrainingMetadataDto TrainingMetadata { get; set; } = new();
}

/// <summary>
/// Antrenman motorunun kullandigi egzersiz metadata'si. Yeni egzersiz olusturmaz; mevcut kayda baglanir.
/// </summary>
public class AdminExerciseTrainingMetadataDto
{
    /// <summary>Compound | Isolation | null (bilinmiyor).</summary>
    public string? MechanicType { get; set; }

    /// <summary>HorizontalPush, VerticalPush, HorizontalPull, VerticalPull, Squat, Hinge, Lunge, Carry, Core, Isolation, Conditioning | null.</summary>
    public string? MovementPattern { get; set; }

    public decimal? LoadIncrementKg { get; set; }
    public List<long> SecondaryMuscleGroupIds { get; set; } = new();
    public List<string> SecondaryMuscleGroupNames { get; set; } = new();
    public List<long> EquipmentIds { get; set; } = new();
    public List<string> EquipmentNames { get; set; } = new();
}

public class AdminUpdateExerciseTrainingMetadataRequest
{
    public ExerciseMechanicType? MechanicType { get; set; }
    public MovementPattern? MovementPattern { get; set; }

    [Range(0, 50, ErrorMessage = "Ağırlık artışı 0-50 kg aralığında olmalıdır.")]
    public decimal? LoadIncrementKg { get; set; }

    public List<long> SecondaryMuscleGroupIds { get; set; } = new();
    public List<long> EquipmentIds { get; set; } = new();
}

/// <summary>Aktif egzersizlerde eksik antrenman metadata'si raporu.</summary>
public class AdminExerciseMetadataAuditDto
{
    public int ActiveExerciseCount { get; set; }
    public int MissingMechanicType { get; set; }
    public int MissingMovementPattern { get; set; }
    public int MissingLoadIncrement { get; set; }
    public int MissingSecondaryMuscles { get; set; }
    public int MissingEquipmentLinks { get; set; }
    public int MissingEquipmentTypeText { get; set; }
    public int MissingImageOrVideo { get; set; }
    public List<AdminExerciseMetadataAuditMuscleDto> ByMuscleGroup { get; set; } = new();

    /// <summary>En fazla 200 egzersiz; eksik alan sayisina gore.</summary>
    public List<AdminExerciseMetadataAuditItemDto> Items { get; set; } = new();
}

public class AdminExerciseMetadataAuditMuscleDto
{
    public long MuscleGroupId { get; set; }
    public string MuscleGroupName { get; set; } = string.Empty;
    public int ActiveExerciseCount { get; set; }
}

public class AdminExerciseMetadataAuditItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PrimaryMuscleGroupName { get; set; } = string.Empty;
    public List<string> Missing { get; set; } = new();
}

public class AdminExerciseListRequest : PagedRequest
{
    public DifficultyLevel? Difficulty { get; set; }
    public long? MuscleGroupId { get; set; }
    public bool? IsActive { get; set; }
}

public class AdminUpsertExerciseRequest
{
    [Required(ErrorMessage = "Isim alani zorunludur.")]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Slug alani zorunludur.")]
    [StringLength(200, MinimumLength = 2)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Slug yalnizca kucuk harf, rakam ve tire (-) icerebilir.")]
    public string Slug { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(8000)]
    public string? Instructions { get; set; }

    [Required(ErrorMessage = "Zorluk seviyesi zorunludur.")]
    public DifficultyLevel Difficulty { get; set; }

    [Required(ErrorMessage = "Ana kas grubu zorunludur.")]
    public long PrimaryMuscleGroupId { get; set; }

    [StringLength(200)]
    public string? EquipmentType { get; set; }

    [StringLength(1000)]
    [Url(ErrorMessage = "Gecerli bir video URL'i giriniz.")]
    public string? VideoUrl { get; set; }

    [StringLength(1000)]
    [Url(ErrorMessage = "Gecerli bir gorsel URL'i giriniz.")]
    public string? ImageUrl { get; set; }

    [Range(0, 100, ErrorMessage = "Dakika basina kalori 0-100 araliginda olmalidir.")]
    public decimal? CaloriesPerMinute { get; set; }

    public bool IsActive { get; set; } = true;
}

public class AdminUpdateExerciseStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}
