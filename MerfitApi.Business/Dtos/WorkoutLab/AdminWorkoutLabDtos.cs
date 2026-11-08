using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Domain.Entities.Enums;

namespace MerfitApi.Business.Dtos.Admin.WorkoutLab;

// Antrenman Laboratuvari. Profil alanlari mobil onboarding ile aynidir; program MerfitCustomerApi'deki gercek
// WorkoutPlanGenerator ile uretilir. Result/QualityChecks alanlari MerfitCustomerApi'nin yazdigi JSON'dur ve
// panele oldugu gibi iletilir (bu API program verisini yeniden yorumlamaz).

/// <summary>Formdaki secenekler ve katalog durumu.</summary>
public class AdminWorkoutLabOptionsDto
{
    public List<AdminWorkoutLabOptionItem> Goals { get; set; } = new();
    public List<AdminWorkoutLabOptionItem> ExperienceLevels { get; set; } = new();
    public List<AdminWorkoutLabOptionItem> TrainingLocations { get; set; } = new();
    public List<AdminWorkoutLabOptionItem> Genders { get; set; } = new();
    public List<AdminWorkoutLabOptionItem> WeekDays { get; set; } = new();
    public List<int> TrainingDaysPerWeekOptions { get; set; } = new();
    public List<int> DurationOptions { get; set; } = new();
    public List<AdminWorkoutLabEquipmentDto> Equipment { get; set; } = new();

    /// <summary>Algoritmanin bos alanlar icin kullandigi varsayilanlar (MerfitCustomerApi appsettings varsayilanlari).</summary>
    public int DefaultTrainingDaysPerWeek { get; set; }
    public int DefaultWorkoutDurationMin { get; set; }
    public int DurationToleranceMin { get; set; }

    public int ActiveWorkoutCount { get; set; }
    public int ActivePremiumWorkoutCount { get; set; }
    public int ActiveScoredWorkoutCount { get; set; }

    /// <summary>En son tamamlanan testin algoritma surumu (henuz test yoksa null).</summary>
    public string? LatestAlgorithmVersion { get; set; }

    public int PendingCount { get; set; }

    /// <summary>En eski bekleyen testin olusturulma zamani; uzun sure beklemesi isleyicinin calismadigini gosterir.</summary>
    public DateTime? OldestPendingAt { get; set; }
}

public class AdminWorkoutLabOptionItem
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class AdminWorkoutLabEquipmentDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

/// <summary>Simule edilecek kullanici tercihleri. Bos birakilan alanlarda algoritmanin varsayilanlari kullanilir.</summary>
public class AdminWorkoutLabProfileInput
{
    [Range(13, 100)]
    public int? Age { get; set; }

    public Gender? Gender { get; set; }

    [Range(100, 250)]
    public decimal? HeightCm { get; set; }

    [Range(30, 300)]
    public decimal? WeightKg { get; set; }

    [Range(30, 300)]
    public decimal? TargetWeightKg { get; set; }

    /// <summary>Yalnizca uc ana hedef: BuildMuscle, GetStronger, LoseWeight. Bos ise Kas Gelistirme.</summary>
    public FitnessGoal? Goal { get; set; }

    public ExperienceLevel? ExperienceLevel { get; set; }

    /// <summary>Home veya Gym (Outdoor da kabul edilir; algoritmada Home gibi ekipman secimiyle calisir).</summary>
    public TrainingLocation TrainingLocation { get; set; } = TrainingLocation.Home;

    /// <summary>Equipment.Id listesi. Gym'de yok sayilir (mobil uygulamadaki gibi tum ekipmanlar kullanilir).</summary>
    public List<long> EquipmentIds { get; set; } = new();

    /// <summary>"Hic ekipmanim yok"; isaretliyse ekipman listesi temizlenir.</summary>
    public bool NoEquipment { get; set; }

    [Range(2, 7)]
    public int? TrainingDaysPerWeek { get; set; }

    /// <summary>Belirli gunler ("Mon".."Sun"). Doluysa gun sayisi bu listeden turetilir (en az 2).</summary>
    public List<string> TrainingDays { get; set; } = new();

    [Range(5, 240)]
    public int? WorkoutDurationMin { get; set; }

    /// <summary>true = aktif Plus aboneligi, false = Free.</summary>
    public bool IsPlus { get; set; }
}

public class AdminCreateWorkoutLabSimulationRequest
{
    [MaxLength(200)]
    public string? Name { get; set; }

    [Required]
    public AdminWorkoutLabProfileInput Profile { get; set; } = new();
}

public class AdminWorkoutLabSimulationListRequest : PagedRequest
{
    public WorkoutPlanSimulationStatus? Status { get; set; }
    public FitnessGoal? Goal { get; set; }
    public ExperienceLevel? ExperienceLevel { get; set; }
    public TrainingLocation? TrainingLocation { get; set; }
    public bool? IsPlus { get; set; }
    public WorkoutPlanEvaluationRating? Rating { get; set; }

    /// <summary>true: yalnizca degerlendirilmis, false: yalnizca degerlendirilmemis.</summary>
    public bool? IsEvaluated { get; set; }

    /// <summary>true: en az bir kalite kontrolu basarisiz olanlar.</summary>
    public bool? HasFailedChecks { get; set; }

    public string? AlgorithmVersion { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
}

public class AdminWorkoutLabSimulationListItemDto
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string ExperienceLevel { get; set; } = string.Empty;
    public int TrainingDaysPerWeek { get; set; }
    public int? WorkoutDurationMin { get; set; }
    public string TrainingLocation { get; set; } = string.Empty;
    public bool IsPlus { get; set; }
    public long? SourceSimulationId { get; set; }
    public string? AlgorithmVersion { get; set; }
    public string? OptionsFingerprint { get; set; }
    public int PassedCheckCount { get; set; }
    public int WarningCheckCount { get; set; }
    public int FailedCheckCount { get; set; }
    public string? Rating { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? CreatedBy { get; set; }
    public string? CreatedByEmail { get; set; }
}

public class AdminWorkoutLabSimulationDetailDto : AdminWorkoutLabSimulationListItemDto
{
    /// <summary>Simule edilen tercihler (MerfitCustomerApi WorkoutPlanSimulationProfile sozlesmesi).</summary>
    public JsonElement? Profile { get; set; }

    /// <summary>Uretilen programin anlik goruntusu; tamamlanmadiysa null.</summary>
    public JsonElement? Result { get; set; }

    /// <summary>Kalite kontrol listesi; tamamlanmadiysa null.</summary>
    public JsonElement? QualityChecks { get; set; }

    public AdminWorkoutLabEvaluationDto? Evaluation { get; set; }
}

public class AdminWorkoutLabEvaluationDto
{
    public string Rating { get; set; } = string.Empty;
    public string? MissingPoints { get; set; }
    public List<AdminWorkoutLabProblemItem> ProblemItems { get; set; } = new();
    public string? ExerciseSuggestions { get; set; }
    public string? Comment { get; set; }
    public DateTime? EvaluatedAt { get; set; }
    public long? EvaluatedBy { get; set; }
    public string? EvaluatedByEmail { get; set; }
}

/// <summary>Sorunlu bulunan gun veya egzersiz.</summary>
public class AdminWorkoutLabProblemItem
{
    /// <summary>"Mon".."Sun".</summary>
    [Required]
    [MaxLength(3)]
    public string DayOfWeek { get; set; } = string.Empty;

    /// <summary>Bos ise sorun tum gun icin.</summary>
    public long? ExerciseId { get; set; }

    [MaxLength(200)]
    public string? ExerciseName { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }
}

public class AdminEvaluateWorkoutLabSimulationRequest
{
    [Required]
    public WorkoutPlanEvaluationRating? Rating { get; set; }

    [MaxLength(4000)]
    public string? MissingPoints { get; set; }

    [MaxLength(50)]
    public List<AdminWorkoutLabProblemItem> ProblemItems { get; set; } = new();

    [MaxLength(4000)]
    public string? ExerciseSuggestions { get; set; }

    [MaxLength(4000)]
    public string? Comment { get; set; }
}

public class AdminWorkoutLabComparisonDto
{
    public AdminWorkoutLabSimulationDetailDto Left { get; set; } = new();
    public AdminWorkoutLabSimulationDetailDto Right { get; set; } = new();

    /// <summary>Iki testin profil tercihleri birebir ayni mi (ayni profil, farkli algoritma surumu karsilastirmasi).</summary>
    public bool SameProfile { get; set; }

    public bool SameAlgorithmVersion { get; set; }
    public bool SameOptionsFingerprint { get; set; }
}
