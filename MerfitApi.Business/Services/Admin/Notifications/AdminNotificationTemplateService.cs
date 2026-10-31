using System.Linq.Expressions;
using MerfitApi.Business.Common.Notifications;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MerfitApi.Business.Services.Admin.Notifications;

/// <summary>
/// Bildirim sablonlari. Sablon metinleri yalnizca turune uygun alanlari ({firstName}, {mealName} ...)
/// icerebilir; yonlendirme verisi izin verilen ekran listesine gore dogrulanir.
/// </summary>
public class AdminNotificationTemplateService : IAdminNotificationTemplateService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;
    private readonly NotificationAdminOptions _options;

    private static readonly IReadOnlyDictionary<string, Expression<Func<NotificationTemplate, object?>>> SortMap =
        new Dictionary<string, Expression<Func<NotificationTemplate, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = x => x.Name,
            ["type"] = x => x.Type,
            ["createdAt"] = x => x.CreatedAt,
        };

    public AdminNotificationTemplateService(IUnitOfWork unitOfWork, IAuditLogService auditLogService, IOptions<NotificationAdminOptions> options)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
        _options = options.Value;
    }

    public async Task<PagedResult<AdminNotificationTemplateDto>> GetListAsync(AdminNotificationTemplateListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<NotificationTemplate>().GetQueryable();
        if (request.Type.HasValue) query = query.Where(x => x.Type == request.Type.Value);
        if (request.App.HasValue)
        {
            var appTypes = NotificationApps.TypesOf(request.App.Value).ToList();
            query = query.Where(x => appTypes.Contains(x.Type));
        }
        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(search) || x.TitleTemplate.ToLower().Contains(search));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "createdAt")
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var ruleCounts = await GetRuleCountsAsync(items.Select(x => x.Id).ToList(), cancellationToken);
        return PagedResult<AdminNotificationTemplateDto>.Create(
            items.Select(x => MapToDto(x, ruleCounts.GetValueOrDefault(x.Id))).ToList(), request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminNotificationTemplateDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);
        var ruleCounts = await GetRuleCountsAsync(new List<long> { id }, cancellationToken);
        return MapToDto(entity, ruleCounts.GetValueOrDefault(id));
    }

    public async Task<AdminNotificationTemplateDto> CreateAsync(AdminUpsertNotificationTemplateRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var dataJson = Validate(request);
        await EnsureUniqueNameAsync(request.Name, null, cancellationToken);

        var entity = new NotificationTemplate
        {
            Name = request.Name.Trim(),
            Type = request.Type,
            TitleTemplate = request.TitleTemplate.Trim(),
            BodyTemplate = request.BodyTemplate.Trim(),
            DataJson = dataJson,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.Repository<NotificationTemplate>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_TEMPLATE_CREATED", nameof(NotificationTemplate), entity.Id, ipAddress, userAgent,
            newValue: new { entity.Name, Type = entity.Type.ToString(), entity.IsActive }, cancellationToken: cancellationToken);

        return MapToDto(entity, 0);
    }

    public async Task<AdminNotificationTemplateDto> UpdateAsync(long id, AdminUpsertNotificationTemplateRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);
        var dataJson = Validate(request);
        await EnsureUniqueNameAsync(request.Name, id, cancellationToken);

        var ruleIds = await _unitOfWork.Repository<NotificationRuleTemplate>().GetQueryable()
            .Where(x => x.NotificationTemplateId == id)
            .Select(x => x.NotificationRuleId)
            .ToListAsync(cancellationToken);

        if (ruleIds.Count > 0 && request.Type != entity.Type)
        {
            throw new ConflictException("Kurallara bagli bir sablonun turu degistirilemez. Once kurallardan cikarin.");
        }

        if (ruleIds.Count > 0 && entity.IsActive && !request.IsActive)
        {
            await EnsureActiveRulesKeepTemplateAsync(id, ruleIds, cancellationToken);
        }

        var oldValue = new { entity.Name, Type = entity.Type.ToString(), entity.TitleTemplate, entity.BodyTemplate, entity.IsActive };

        entity.Name = request.Name.Trim();
        entity.Type = request.Type;
        entity.TitleTemplate = request.TitleTemplate.Trim();
        entity.BodyTemplate = request.BodyTemplate.Trim();
        entity.DataJson = dataJson;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        _unitOfWork.Repository<NotificationTemplate>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_TEMPLATE_UPDATED", nameof(NotificationTemplate), id, ipAddress, userAgent,
            oldValue: oldValue, newValue: new { entity.Name, Type = entity.Type.ToString(), entity.TitleTemplate, entity.BodyTemplate, entity.IsActive },
            cancellationToken: cancellationToken);

        return MapToDto(entity, ruleIds.Count);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);

        var inRules = await _unitOfWork.Repository<NotificationRuleTemplate>().AnyAsync(x => x.NotificationTemplateId == id, cancellationToken);
        if (inRules)
        {
            throw new ConflictException("Sablon bir veya daha fazla kuralda kullaniliyor. Once kurallardan cikarin ya da pasif yapin.");
        }

        var inOpenCampaigns = await _unitOfWork.Repository<NotificationCampaign>().AnyAsync(x => x.NotificationTemplateId == id
            && (x.Status == NotificationCampaignStatus.Draft || x.Status == NotificationCampaignStatus.Scheduled), cancellationToken);
        if (inOpenCampaigns)
        {
            throw new ConflictException("Sablon taslak veya zamanlanmis bir kampanyada kullaniliyor.");
        }

        // Gecmis gonderimlerdeki NotificationTemplateId alanini veritabani SET NULL yapar.
        _unitOfWork.Repository<NotificationTemplate>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_TEMPLATE_DELETED", nameof(NotificationTemplate), id, ipAddress, userAgent,
            oldValue: new { entity.Name, Type = entity.Type.ToString() }, cancellationToken: cancellationToken);
    }

    public async Task<AdminNotificationMessagePreviewDto> PreviewAsync(AdminNotificationMessagePreviewRequest request, CancellationToken cancellationToken = default)
    {
        var disallowed = NotificationContentRules.FindDisallowedPlaceholders(request.Type, request.Title, request.Body);
        if (disallowed.Count > 0)
        {
            throw new AppValidationException("body", $"Bu turde kullanilamayan alanlar: {string.Join(", ", disallowed.Select(x => "{" + x + "}"))}.");
        }

        var dataJson = NotificationNavigation.Normalize(request.DataJson, out var error);
        if (error is not null)
        {
            throw new AppValidationException(nameof(request.DataJson), error);
        }

        string? firstName = null;
        if (request.UserId.HasValue)
        {
            firstName = await _unitOfWork.Repository<UserProfile>().GetQueryable()
                .Where(p => p.UserId == request.UserId.Value)
                .Select(p => p.FirstName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var values = NotificationContentRules.SampleValues(firstName);
        return new AdminNotificationMessagePreviewDto
        {
            Title = NotificationTemplateRenderer.Render(request.Title, values),
            Body = NotificationTemplateRenderer.Render(request.Body, values),
            Data = NotificationNavigation.ToDictionary(dataJson),
            AllowedPlaceholders = NotificationContentRules.AllowedPlaceholders(request.Type).OrderBy(x => x).ToList(),
        };
    }

    public AdminNotificationMetadataDto GetMetadata() => new()
    {
        Types = Enum.GetNames<NotificationType>(),
        CampaignTypes = NotificationContentRules.CampaignTypes.Select(x => x.ToString()).OrderBy(x => x).ToList(),
        RuleKinds = Enum.GetNames<NotificationRuleKind>(),
        Segments = Enum.GetNames<NotificationAudienceSegment>(),
        Screens = NotificationNavigation.AllowedScreens.OrderBy(x => x).ToList(),
        Apps = Enum.GetNames<NotificationApp>(),
        AppByType = Enum.GetValues<NotificationType>().ToDictionary(t => t.ToString(), t => NotificationApps.Of(t).ToString()),
        ScreensByApp = new Dictionary<string, IReadOnlyList<string>>
        {
            [nameof(NotificationApp.MbFit)] = NotificationNavigation.AllowedScreens.Where(s => !NotificationNavigation.ShopScreens.Contains(s)).OrderBy(x => x).ToList(),
            [nameof(NotificationApp.MbShop)] = NotificationNavigation.ShopScreens.OrderBy(x => x).ToList(),
        },
        PlaceholdersByType = Enum.GetValues<NotificationType>().ToDictionary(
            t => t.ToString(),
            t => (IReadOnlyList<string>)NotificationContentRules.AllowedPlaceholders(t).OrderBy(x => x).ToList()),
        LargeAudienceThreshold = _options.LargeAudienceThreshold,
        MaxSelectedUsers = _options.MaxSelectedUsers,
        DefaultTimeZone = _options.DefaultTimeZone,
    };

    /// <summary>Metin alanlarini ve yonlendirme verisini dogrular; normalize edilmis DataJson'u dondurur.</summary>
    public static string? Validate(AdminUpsertNotificationTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.TitleTemplate) || string.IsNullOrWhiteSpace(request.BodyTemplate))
        {
            throw new AppValidationException("Ad, baslik ve icerik bos olamaz.");
        }

        var disallowed = NotificationContentRules.FindDisallowedPlaceholders(request.Type, request.TitleTemplate, request.BodyTemplate);
        if (disallowed.Count > 0)
        {
            var allowed = string.Join(", ", NotificationContentRules.AllowedPlaceholders(request.Type).Select(x => "{" + x + "}"));
            throw new AppValidationException(nameof(request.BodyTemplate),
                $"Bu turde kullanilamayan alanlar: {string.Join(", ", disallowed.Select(x => "{" + x + "}"))}. Kullanilabilecek alanlar: {allowed}.");
        }

        var dataJson = NotificationNavigation.Normalize(request.DataJson, out var error);
        error ??= NotificationNavigation.ValidateApp(dataJson, NotificationApps.Of(request.Type));
        if (error is not null)
        {
            throw new AppValidationException(nameof(request.DataJson), error);
        }

        return dataJson;
    }

    /// <summary>Pasife alinan sablon, aktif bir kuralin son aktif sablonu ise reddedilir.</summary>
    private async Task EnsureActiveRulesKeepTemplateAsync(long templateId, List<long> ruleIds, CancellationToken cancellationToken)
    {
        var activeRuleIds = await _unitOfWork.Repository<NotificationRule>().GetQueryable()
            .Where(r => ruleIds.Contains(r.Id) && r.Status == NotificationRuleStatus.Active)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        if (activeRuleIds.Count == 0)
        {
            return;
        }

        var templates = _unitOfWork.Repository<NotificationTemplate>().GetQueryable();
        var otherActive = await _unitOfWork.Repository<NotificationRuleTemplate>().GetQueryable()
            .Where(rt => activeRuleIds.Contains(rt.NotificationRuleId) && rt.NotificationTemplateId != templateId)
            .Where(rt => templates.Any(t => t.Id == rt.NotificationTemplateId && t.IsActive))
            .Select(rt => rt.NotificationRuleId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var orphaned = activeRuleIds.Except(otherActive).ToList();
        if (orphaned.Count > 0)
        {
            throw new ConflictException($"Bu sablon aktif kurallarin ({string.Join(", ", orphaned)}) tek aktif sablonu. Once kurali duraklatin veya baska sablon ekleyin.");
        }
    }

    private async Task EnsureUniqueNameAsync(string name, long? excludeId, CancellationToken cancellationToken)
    {
        var normalized = name.Trim();
        var exists = await _unitOfWork.Repository<NotificationTemplate>()
            .AnyAsync(x => x.Name == normalized && (!excludeId.HasValue || x.Id != excludeId.Value), cancellationToken);
        if (exists)
        {
            throw new ConflictException("Bu adla bir sablon zaten var.");
        }
    }

    private async Task<Dictionary<long, int>> GetRuleCountsAsync(List<long> templateIds, CancellationToken cancellationToken)
    {
        if (templateIds.Count == 0)
        {
            return new Dictionary<long, int>();
        }

        var links = await _unitOfWork.Repository<NotificationRuleTemplate>().GetQueryable()
            .Where(x => templateIds.Contains(x.NotificationTemplateId))
            .Select(x => x.NotificationTemplateId)
            .ToListAsync(cancellationToken);
        return links.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
    }

    private async Task<NotificationTemplate> GetOrThrowAsync(long id, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<NotificationTemplate>().GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException(nameof(NotificationTemplate), id);

    private static AdminNotificationTemplateDto MapToDto(NotificationTemplate entity, int ruleCount) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Type = entity.Type.ToString(),
        App = NotificationApps.Of(entity.Type).ToString(),
        TitleTemplate = entity.TitleTemplate,
        BodyTemplate = entity.BodyTemplate,
        DataJson = entity.DataJson,
        IsActive = entity.IsActive,
        RuleCount = ruleCount,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };
}
