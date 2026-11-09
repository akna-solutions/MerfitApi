using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.WorkoutLab;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>
/// Antrenman Laboratuvari (admin). Test kaydini Pending olarak acar; programi bu API uretmez. MerfitCustomerApi'deki
/// WorkoutPlanSimulationBackgroundWorker kaydi alip GERCEK WorkoutPlanGenerator'i (ayni algoritma, ayni aktif katalog)
/// calistirir ve sonucu ayni satira yazar. Boylece algoritmanin ikinci bir kopyasi olusmaz.
///
/// Gercek kullanici verisine dokunulmaz: bu servis yalnizca WorkoutPlanSimulation tablosunu yazar ve katalog
/// tablolarini (Equipment, Workout, WorkoutAttributeScore) okur.
/// </summary>
public class AdminWorkoutLabService : IAdminWorkoutLabService
{
    // MerfitCustomerApi WorkoutPlanGenerationOptions varsayilanlari (bos alanlar icin bilgi amacli gosterilir;
    // gercekte kullanilan degerler her testin sonucunda yazar).
    public const int DefaultWorkoutDurationMin = 45;
    public const int DurationToleranceMin = 15;

    private static readonly int[] DurationOptions = { 15, 20, 30, 45, 60, 75, 90 };
    private static readonly string[] DayCodes = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    private static readonly FitnessGoal[] PrimaryGoals = { FitnessGoal.BuildMuscle, FitnessGoal.GetStronger, FitnessGoal.LoseWeight };

    /// <summary>MerfitCustomerApi WorkoutLabJson ile ayni bicim: camelCase, enum'lar ad olarak.</summary>
    internal static readonly JsonSerializerOptions ProfileJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly IReadOnlyDictionary<string, Expression<Func<WorkoutPlanSimulation, object?>>> SortMap =
        new Dictionary<string, Expression<Func<WorkoutPlanSimulation, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = x => x.CreatedAt,
            ["id"] = x => x.Id,
            ["failedCheckCount"] = x => x.FailedCheckCount,
            ["algorithmVersion"] = x => x.AlgorithmVersion,
        };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminWorkoutLabService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<AdminWorkoutLabOptionsDto> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        var equipment = await _unitOfWork.Repository<Equipment>().GetQueryable()
            .OrderBy(e => e.Name)
            .Select(e => new AdminWorkoutLabEquipmentDto { Id = e.Id, Name = e.Name, Slug = e.Slug })
            .ToListAsync(cancellationToken);

        var workouts = _unitOfWork.Repository<Workout>().GetQueryable().Where(w => w.IsActive);
        var scores = _unitOfWork.Repository<WorkoutAttributeScore>().GetQueryable();
        var simulations = _unitOfWork.Repository<WorkoutPlanSimulation>().GetQueryable();

        var pending = simulations.Where(s => s.Status == WorkoutPlanSimulationStatus.Pending);

        return new AdminWorkoutLabOptionsDto
        {
            Goals = PrimaryGoals.Select(g => Item(g.ToString(), GoalLabel(g))).ToList(),
            ExperienceLevels = new List<AdminWorkoutLabOptionItem>
            {
                Item(nameof(ExperienceLevel.Beginner), "Başlangıç"),
                Item(nameof(ExperienceLevel.Intermediate), "Orta"),
                Item(nameof(ExperienceLevel.Advanced), "İleri"),
            },
            TrainingLocations = new List<AdminWorkoutLabOptionItem>
            {
                Item(nameof(TrainingLocation.Home), "Ev"),
                Item(nameof(TrainingLocation.Gym), "Spor salonu"),
            },
            Genders = new List<AdminWorkoutLabOptionItem>
            {
                Item(nameof(Gender.Male), "Erkek"),
                Item(nameof(Gender.Female), "Kadın"),
            },
            WeekDays = DayCodes.Zip(new[] { "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar" })
                .Select(x => Item(x.First, x.Second))
                .ToList(),
            DurationOptions = DurationOptions.ToList(),
            Equipment = equipment,
            // MerfitCustomerApi TrainingDayPlanner kurali (bilgi amacli).
            TargetTrainingDaysByExperience = new Dictionary<string, int>
            {
                [nameof(ExperienceLevel.Beginner)] = 3,
                [nameof(ExperienceLevel.Intermediate)] = 4,
                [nameof(ExperienceLevel.Advanced)] = 5,
            },
            LoseWeightExtraTrainingDays = 1,
            DefaultWorkoutDurationMin = DefaultWorkoutDurationMin,
            DurationToleranceMin = DurationToleranceMin,
            ActiveWorkoutCount = await workouts.CountAsync(cancellationToken),
            ActivePremiumWorkoutCount = await workouts.CountAsync(w => w.IsPremium, cancellationToken),
            ActiveScoredWorkoutCount = await workouts.CountAsync(w => scores.Any(s => s.WorkoutId == w.Id), cancellationToken),
            LatestAlgorithmVersion = await simulations
                .Where(s => s.AlgorithmVersion != null)
                .OrderByDescending(s => s.Id)
                .Select(s => s.AlgorithmVersion)
                .FirstOrDefaultAsync(cancellationToken),
            PendingCount = await pending.CountAsync(cancellationToken),
            OldestPendingAt = await pending.OrderBy(s => s.Id).Select(s => (DateTime?)s.CreatedAt).FirstOrDefaultAsync(cancellationToken),
        };
    }

    public async Task<AdminWorkoutLabSimulationDetailDto> CreateAsync(
        AdminCreateWorkoutLabSimulationRequest request,
        long? adminUserId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var profile = await NormalizeProfileAsync(request.Profile, cancellationToken);
        var simulation = NewSimulation(Trim(request.Name), profile, adminUserId, sourceId: null);

        await _unitOfWork.Repository<WorkoutPlanSimulation>().AddAsync(simulation, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_LAB_SIMULATION_CREATED", nameof(WorkoutPlanSimulation), simulation.Id,
            ipAddress, userAgent, newValue: new { simulation.Name, Profile = profile }, cancellationToken: cancellationToken);

        return await GetByIdAsync(simulation.Id, cancellationToken);
    }

    public async Task<AdminWorkoutLabSimulationDetailDto> RerunAsync(
        long id,
        long? adminUserId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var source = await _unitOfWork.Repository<WorkoutPlanSimulation>().GetQueryable()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutPlanSimulation), id);

        var copy = new WorkoutPlanSimulation
        {
            Name = source.Name,
            Status = WorkoutPlanSimulationStatus.Pending,
            ProfileJson = source.ProfileJson,
            Goal = source.Goal,
            ExperienceLevel = source.ExperienceLevel,
            TrainingDaysPerWeek = 0,
            WorkoutDurationMin = source.WorkoutDurationMin,
            TrainingLocation = source.TrainingLocation,
            IsPlus = source.IsPlus,
            SourceSimulationId = source.Id,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.Repository<WorkoutPlanSimulation>().AddAsync(copy, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_LAB_SIMULATION_RERUN", nameof(WorkoutPlanSimulation), copy.Id,
            ipAddress, userAgent, newValue: new { SourceSimulationId = source.Id }, cancellationToken: cancellationToken);

        return await GetByIdAsync(copy.Id, cancellationToken);
    }

    public async Task<PagedResult<AdminWorkoutLabSimulationListItemDto>> GetListAsync(
        AdminWorkoutLabSimulationListRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<WorkoutPlanSimulation>().GetQueryable();

        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
        if (request.Goal.HasValue) query = query.Where(x => x.Goal == request.Goal.Value);
        if (request.ExperienceLevel.HasValue) query = query.Where(x => x.ExperienceLevel == request.ExperienceLevel.Value);
        if (request.TrainingLocation.HasValue) query = query.Where(x => x.TrainingLocation == request.TrainingLocation.Value);
        if (request.IsPlus.HasValue) query = query.Where(x => x.IsPlus == request.IsPlus.Value);
        if (request.Rating.HasValue) query = query.Where(x => x.Rating == request.Rating.Value);
        if (request.IsEvaluated.HasValue) query = query.Where(x => (x.Rating != null) == request.IsEvaluated.Value);
        if (request.HasFailedChecks == true) query = query.Where(x => x.FailedCheckCount > 0);
        if (request.HasFailedChecks == false) query = query.Where(x => x.FailedCheckCount == 0);
        if (!string.IsNullOrWhiteSpace(request.AlgorithmVersion))
        {
            var version = request.AlgorithmVersion.Trim();
            query = query.Where(x => x.AlgorithmVersion == version);
        }

        if (request.CreatedFrom.HasValue) query = query.Where(x => x.CreatedAt >= request.CreatedFrom.Value);
        if (request.CreatedTo.HasValue) query = query.Where(x => x.CreatedAt <= request.CreatedTo.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = long.TryParse(term, out var idTerm)
                ? query.Where(x => x.Id == idTerm || (x.Name != null && x.Name.Contains(term)))
                : query.Where(x => x.Name != null && x.Name.Contains(term));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        // Liste JSON kolonlarini okumaz; yalnizca ozet kolonlari ve olusturan admin'in e-postasi (tek sorgu).
        var rows = await query
            .ApplySort(request.SortBy, request.SortDirection is null || request.IsDescending, SortMap, defaultKey: "id")
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                Simulation = new WorkoutPlanSimulation
                {
                    Id = x.Id,
                    Name = x.Name,
                    Status = x.Status,
                    Goal = x.Goal,
                    ExperienceLevel = x.ExperienceLevel,
                    TrainingDaysPerWeek = x.TrainingDaysPerWeek,
                    WorkoutDurationMin = x.WorkoutDurationMin,
                    TrainingLocation = x.TrainingLocation,
                    IsPlus = x.IsPlus,
                    SourceSimulationId = x.SourceSimulationId,
                    AlgorithmVersion = x.AlgorithmVersion,
                    OptionsFingerprint = x.OptionsFingerprint,
                    PassedCheckCount = x.PassedCheckCount,
                    WarningCheckCount = x.WarningCheckCount,
                    FailedCheckCount = x.FailedCheckCount,
                    Rating = x.Rating,
                    ErrorMessage = x.ErrorMessage,
                    CreatedAt = x.CreatedAt,
                    CompletedAt = x.CompletedAt,
                    CreatedUser = x.CreatedUser,
                },
                CreatedByEmail = users.Where(u => u.Id == x.CreatedUser).Select(u => u.Email).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r =>
        {
            var dto = new AdminWorkoutLabSimulationListItemDto();
            FillListItem(dto, r.Simulation, r.CreatedByEmail);
            return dto;
        }).ToList();

        return PagedResult<AdminWorkoutLabSimulationListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminWorkoutLabSimulationDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var simulation = await _unitOfWork.Repository<WorkoutPlanSimulation>().GetQueryable()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutPlanSimulation), id);

        var userIds = new[] { simulation.CreatedUser, simulation.EvaluatedBy }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var emails = userIds.Count == 0
            ? new Dictionary<long, string>()
            : await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        var dto = new AdminWorkoutLabSimulationDetailDto
        {
            Profile = ParseJson(simulation.ProfileJson),
            Result = ParseJson(simulation.ResultJson),
            QualityChecks = ParseJson(simulation.QualityChecksJson),
            Evaluation = simulation.Rating is null
                ? null
                : new AdminWorkoutLabEvaluationDto
                {
                    Rating = simulation.Rating.Value.ToString(),
                    MissingPoints = simulation.EvaluationMissingPoints,
                    ProblemItems = string.IsNullOrWhiteSpace(simulation.EvaluationProblemItemsJson)
                        ? new List<AdminWorkoutLabProblemItem>()
                        : JsonSerializer.Deserialize<List<AdminWorkoutLabProblemItem>>(simulation.EvaluationProblemItemsJson, ProfileJsonOptions) ?? new(),
                    ExerciseSuggestions = simulation.EvaluationExerciseSuggestions,
                    Comment = simulation.EvaluationComment,
                    EvaluatedAt = simulation.EvaluatedAt,
                    EvaluatedBy = simulation.EvaluatedBy,
                    EvaluatedByEmail = simulation.EvaluatedBy is long by ? emails.GetValueOrDefault(by) : null,
                },
        };
        FillListItem(dto, simulation, simulation.CreatedUser is long createdBy ? emails.GetValueOrDefault(createdBy) : null);
        return dto;
    }

    public async Task<AdminWorkoutLabSimulationDetailDto> EvaluateAsync(
        long id,
        AdminEvaluateWorkoutLabSimulationRequest request,
        long? adminUserId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<WorkoutPlanSimulation>();
        var simulation = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutPlanSimulation), id);

        if (simulation.Status is WorkoutPlanSimulationStatus.Pending or WorkoutPlanSimulationStatus.Processing)
        {
            throw new AppValidationException("Program henüz üretilmedi; değerlendirme test tamamlandıktan sonra yapılabilir.");
        }

        if (request.Rating is null)
        {
            throw new AppValidationException(nameof(request.Rating), "Değerlendirme seçin.");
        }

        var items = (request.ProblemItems ?? new List<AdminWorkoutLabProblemItem>()).ToList();
        if (items.Count > 50)
        {
            throw new AppValidationException(nameof(request.ProblemItems), "En fazla 50 sorunlu gün/egzersiz eklenebilir.");
        }

        foreach (var item in items)
        {
            if (!DayCodes.Contains(item.DayOfWeek))
            {
                throw new AppValidationException(nameof(request.ProblemItems), $"Geçersiz gün: {item.DayOfWeek}. Geçerli değerler: {string.Join(", ", DayCodes)}.");
            }

            item.ExerciseName = Trim(item.ExerciseName);
            item.Note = Trim(item.Note);
        }

        var old = new { simulation.Rating, simulation.EvaluationMissingPoints, simulation.EvaluationExerciseSuggestions, simulation.EvaluationComment };

        simulation.Rating = request.Rating.Value;
        simulation.EvaluationMissingPoints = Trim(request.MissingPoints);
        simulation.EvaluationProblemItemsJson = items.Count == 0 ? null : JsonSerializer.Serialize(items, ProfileJsonOptions);
        simulation.EvaluationExerciseSuggestions = Trim(request.ExerciseSuggestions);
        simulation.EvaluationComment = Trim(request.Comment);
        simulation.EvaluatedAt = DateTime.UtcNow;
        simulation.EvaluatedBy = adminUserId;
        simulation.UpdatedAt = DateTime.UtcNow;
        simulation.UpdatedUser = adminUserId;
        repo.Update(simulation);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Test bu sırada güncellendi. Sayfayı yenileyip tekrar deneyin.");
        }

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_LAB_SIMULATION_EVALUATED", nameof(WorkoutPlanSimulation), id,
            ipAddress, userAgent, oldValue: old, newValue: new { simulation.Rating, ProblemItemCount = items.Count }, cancellationToken: cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<WorkoutPlanSimulation>();
        var simulation = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutPlanSimulation), id);

        if (simulation.Status == WorkoutPlanSimulationStatus.Processing)
        {
            throw new ConflictException("Test şu anda işleniyor; tamamlandıktan sonra silebilirsiniz.");
        }

        repo.Remove(simulation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "WORKOUT_LAB_SIMULATION_DELETED", nameof(WorkoutPlanSimulation), id,
            ipAddress, userAgent, oldValue: new { simulation.Name, simulation.Status, simulation.AlgorithmVersion }, cancellationToken: cancellationToken);
    }

    public async Task<AdminWorkoutLabComparisonDto> CompareAsync(long leftId, long rightId, CancellationToken cancellationToken = default)
    {
        if (leftId == rightId)
        {
            throw new AppValidationException("Karşılaştırmak için iki farklı test seçin.");
        }

        var left = await GetByIdAsync(leftId, cancellationToken);
        var right = await GetByIdAsync(rightId, cancellationToken);
        if (left.Result is null || right.Result is null)
        {
            throw new AppValidationException("Yalnızca program üretilmiş (tamamlanmış) testler karşılaştırılabilir.");
        }

        return new AdminWorkoutLabComparisonDto
        {
            Left = left,
            Right = right,
            SameProfile = JsonNode.DeepEquals(
                JsonNode.Parse(left.Profile?.GetRawText() ?? "{}"),
                JsonNode.Parse(right.Profile?.GetRawText() ?? "{}")),
            SameAlgorithmVersion = left.AlgorithmVersion == right.AlgorithmVersion,
            SameOptionsFingerprint = left.OptionsFingerprint == right.OptionsFingerprint,
        };
    }

    /// <summary>
    /// Profili dogrular ve mobil uygulama/gercek sistemle ayni kurallara getirir: yalnizca uc ana hedef; ekipmanli
    /// (tum katalog) / ekipmansiz secimi, spor salonu her zaman ekipmanli; musait gunler bos (tum hafta) veya en az
    /// 2 gun. Haftalik gun sayisini algoritma belirler; formdan alinmaz.
    /// </summary>
    internal async Task<WorkoutLabProfileJson> NormalizeProfileAsync(AdminWorkoutLabProfileInput input, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            throw new AppValidationException("Profile", "Profil bilgileri zorunludur.");
        }

        if (input.Goal is { } goal && !PrimaryGoals.Contains(goal))
        {
            throw new AppValidationException(nameof(input.Goal), "Yalnızca Kas Geliştirme (BuildMuscle), Güçlenme (GetStronger) veya Yağ Kaybı (LoseWeight) seçilebilir.");
        }

        if (input.Gender is Gender.Other)
        {
            throw new AppValidationException(nameof(input.Gender), "Cinsiyet Erkek veya Kadın olmalıdır.");
        }

        var days = new List<string>();
        foreach (var raw in input.TrainingDays ?? new List<string>())
        {
            var code = DayCodes.FirstOrDefault(c => string.Equals(c, raw?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new AppValidationException(nameof(input.TrainingDays), $"Geçersiz gün: {raw}. Geçerli değerler: {string.Join(", ", DayCodes)}.");
            if (!days.Contains(code))
            {
                days.Add(code);
            }
        }

        if (days.Count == 1)
        {
            throw new AppValidationException(nameof(input.TrainingDays), "En az 2 gün seçmelisiniz (mobil uygulamadaki kural).");
        }

        days = days.OrderBy(d => Array.IndexOf(DayCodes, d)).ToList();

        // Spor salonu her zaman ekipmanlidir. Eski istemciler HasEquipment gondermezse ekipman listesinden turetilir.
        var hasEquipment = input.TrainingLocation == TrainingLocation.Gym
            || (input.HasEquipment ?? (!input.NoEquipment && (input.EquipmentIds?.Count ?? 0) > 0));

        return new WorkoutLabProfileJson
        {
            Age = input.Age,
            Gender = input.Gender,
            HeightCm = input.HeightCm,
            WeightKg = input.WeightKg,
            TargetWeightKg = input.TargetWeightKg,
            Goal = input.Goal,
            ExperienceLevel = input.ExperienceLevel,
            TrainingLocation = input.TrainingLocation,
            HasEquipment = hasEquipment,
            NoEquipment = !hasEquipment,
            TrainingDays = days,
            WorkoutDurationMin = input.WorkoutDurationMin,
            IsPlus = input.IsPlus,
        };
    }

    private static WorkoutPlanSimulation NewSimulation(string? name, WorkoutLabProfileJson profile, long? adminUserId, long? sourceId) => new()
    {
        Name = name,
        Status = WorkoutPlanSimulationStatus.Pending,
        ProfileJson = JsonSerializer.Serialize(profile, ProfileJsonOptions),
        Goal = profile.Goal ?? FitnessGoal.BuildMuscle,
        ExperienceLevel = profile.ExperienceLevel ?? ExperienceLevel.Beginner,
        // Gun sayisini algoritma belirler; isleyici test tamamlaninca gercek degeri yazar.
        TrainingDaysPerWeek = 0,
        WorkoutDurationMin = profile.WorkoutDurationMin,
        TrainingLocation = profile.TrainingLocation,
        IsPlus = profile.IsPlus,
        SourceSimulationId = sourceId,
        CreatedAt = DateTime.UtcNow,
        CreatedUser = adminUserId,
    };

    private static void FillListItem(AdminWorkoutLabSimulationListItemDto dto, WorkoutPlanSimulation s, string? createdByEmail)
    {
        dto.Id = s.Id;
        dto.Name = s.Name;
        dto.Status = s.Status.ToString();
        dto.Goal = s.Goal.ToString();
        dto.ExperienceLevel = s.ExperienceLevel.ToString();
        dto.TrainingDaysPerWeek = s.TrainingDaysPerWeek;
        dto.WorkoutDurationMin = s.WorkoutDurationMin;
        dto.TrainingLocation = s.TrainingLocation.ToString();
        dto.IsPlus = s.IsPlus;
        dto.SourceSimulationId = s.SourceSimulationId;
        dto.AlgorithmVersion = s.AlgorithmVersion;
        dto.OptionsFingerprint = s.OptionsFingerprint;
        dto.PassedCheckCount = s.PassedCheckCount;
        dto.WarningCheckCount = s.WarningCheckCount;
        dto.FailedCheckCount = s.FailedCheckCount;
        dto.Rating = s.Rating?.ToString();
        dto.ErrorMessage = s.ErrorMessage;
        dto.CreatedAt = s.CreatedAt;
        dto.CompletedAt = s.CompletedAt;
        dto.CreatedBy = s.CreatedUser;
        dto.CreatedByEmail = createdByEmail;
    }

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static AdminWorkoutLabOptionItem Item(string value, string label) => new() { Value = value, Label = label };

    private static string GoalLabel(FitnessGoal goal) => goal switch
    {
        FitnessGoal.LoseWeight => "Yağ Kaybı",
        FitnessGoal.GetStronger => "Güçlenme",
        _ => "Kas Geliştirme",
    };

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// ProfileJson sozlesmesi. MerfitCustomerApi'deki WorkoutPlanSimulationProfile ile ayni alan adlari ve enum
/// bicimi kullanilir (isleyici bu JSON'u okur).
/// </summary>
public sealed class WorkoutLabProfileJson
{
    public int? Age { get; set; }
    public Gender? Gender { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? TargetWeightKg { get; set; }
    public FitnessGoal? Goal { get; set; }
    public ExperienceLevel? ExperienceLevel { get; set; }
    public TrainingLocation TrainingLocation { get; set; }

    /// <summary>true: tum katalog, false: yalnizca vucut agirligi. Eski kayitlarda null olabilir.</summary>
    public bool? HasEquipment { get; set; }

    /// <summary>Eski kayitlar icin (2026.11-v2 oncesi). Yeni kayitlarda bos.</summary>
    public List<long> EquipmentIds { get; set; } = new();
    public bool NoEquipment { get; set; }
    public int? TrainingDaysPerWeek { get; set; }

    /// <summary>Musait gunler; bos ise tum hafta.</summary>
    public List<string> TrainingDays { get; set; } = new();
    public int? WorkoutDurationMin { get; set; }
    public bool IsPlus { get; set; }
}
