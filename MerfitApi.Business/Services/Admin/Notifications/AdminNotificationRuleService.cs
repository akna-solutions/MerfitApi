using System.Globalization;
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

namespace MerfitApi.Business.Services.Admin.Notifications;

/// <summary>
/// Otomatik bildirim kurallari (beslenme / antrenman hatirlatici, gunluk motivasyon). Kurallar burada
/// tanimlanir; planlama ve gonderim MerfitCustomerApi'deki NotificationRulePlanner ile yapilir.
/// Yeni kural Paused olarak olusur; Active yapmak icin kural turune uygun en az bir aktif sablon gerekir.
/// </summary>
public class AdminNotificationRuleService : IAdminNotificationRuleService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<NotificationRule, object?>>> SortMap =
        new Dictionary<string, Expression<Func<NotificationRule, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = x => x.Name,
            ["kind"] = x => x.Kind,
            ["localTime"] = x => x.LocalTime,
            ["createdAt"] = x => x.CreatedAt,
        };

    public AdminNotificationRuleService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminNotificationRuleDto>> GetListAsync(AdminNotificationRuleListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<NotificationRule>().GetQueryable();
        if (request.Kind.HasValue) query = query.Where(x => x.Kind == request.Kind.Value);
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(search));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "createdAt")
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var templates = await GetTemplateLinksAsync(items.Select(x => x.Id).ToList(), cancellationToken);
        return PagedResult<AdminNotificationRuleDto>.Create(items.Select(x => MapToDto(x, templates)).ToList(), request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminNotificationRuleDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);
        return MapToDto(entity, await GetTemplateLinksAsync(new List<long> { id }, cancellationToken));
    }

    public async Task<AdminNotificationRuleDto> CreateAsync(AdminUpsertNotificationRuleRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var (localTime, templateIds) = await ValidateAsync(request, cancellationToken);

        var now = DateTime.UtcNow;
        var entity = new NotificationRule
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Kind = request.Kind,
            Status = NotificationRuleStatus.Paused,
            LocalTime = localTime,
            DaysOfWeekMask = request.DaysOfWeekMask,
            MealType = request.Kind == NotificationRuleKind.NutritionReminder ? request.MealType : null,
            OnlyUsersWithPlan = request.OnlyUsersWithPlan,
            CreatedAt = now,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        await _unitOfWork.Repository<NotificationRule>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _unitOfWork.Repository<NotificationRuleTemplate>().AddRangeAsync(templateIds.Select(t => new NotificationRuleTemplate
        {
            NotificationRuleId = entity.Id,
            NotificationTemplateId = t,
            CreatedAt = now,
            CreatedUser = adminUserId,
        }), cancellationToken);
        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_RULE_CREATED", nameof(NotificationRule), entity.Id, ipAddress, userAgent,
            newValue: Snapshot(entity, templateIds), cancellationToken: cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<AdminNotificationRuleDto> UpdateAsync(long id, AdminUpsertNotificationRuleRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);
        var (localTime, templateIds) = await ValidateAsync(request, cancellationToken);

        if (entity.Status == NotificationRuleStatus.Active)
        {
            await EnsureHasActiveTemplateAsync(request.Kind, templateIds, cancellationToken);
        }

        var linkRepo = _unitOfWork.Repository<NotificationRuleTemplate>();
        var existingLinks = await linkRepo.GetQueryable(asNoTracking: false).Where(x => x.NotificationRuleId == id).ToListAsync(cancellationToken);
        var oldValue = Snapshot(entity, existingLinks.Select(x => x.NotificationTemplateId).ToList());
        var now = DateTime.UtcNow;

        entity.Name = request.Name.Trim();
        entity.Description = request.Description?.Trim();
        entity.Kind = request.Kind;
        entity.LocalTime = localTime;
        entity.DaysOfWeekMask = request.DaysOfWeekMask;
        entity.MealType = request.Kind == NotificationRuleKind.NutritionReminder ? request.MealType : null;
        entity.OnlyUsersWithPlan = request.OnlyUsersWithPlan;
        entity.UpdatedAt = now;
        entity.UpdatedUser = adminUserId;
        _unitOfWork.Repository<NotificationRule>().Update(entity);

        linkRepo.RemoveRange(existingLinks.Where(x => !templateIds.Contains(x.NotificationTemplateId)));
        var existingIds = existingLinks.Select(x => x.NotificationTemplateId).ToHashSet();
        await linkRepo.AddRangeAsync(templateIds.Where(t => !existingIds.Contains(t)).Select(t => new NotificationRuleTemplate
        {
            NotificationRuleId = id,
            NotificationTemplateId = t,
            CreatedAt = now,
            CreatedUser = adminUserId,
        }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_RULE_UPDATED", nameof(NotificationRule), id, ipAddress, userAgent,
            oldValue: oldValue, newValue: Snapshot(entity, templateIds), cancellationToken: cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<AdminNotificationRuleDto> ChangeStatusAsync(long id, NotificationRuleStatus status, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);
        var oldStatus = entity.Status;
        if (oldStatus == status)
        {
            return await GetByIdAsync(id, cancellationToken);
        }

        if (status == NotificationRuleStatus.Active)
        {
            var templateIds = await _unitOfWork.Repository<NotificationRuleTemplate>().GetQueryable()
                .Where(x => x.NotificationRuleId == id).Select(x => x.NotificationTemplateId).ToListAsync(cancellationToken);
            await EnsureHasActiveTemplateAsync(entity.Kind, templateIds, cancellationToken);
        }

        // Duraklatilan/pasif yapilan kuralin bekleyen gonderimleri gonderim aninda iptal edilir (RuleNotActive).
        entity.Status = status;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;
        _unitOfWork.Repository<NotificationRule>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_RULE_STATUS_CHANGED", nameof(NotificationRule), id, ipAddress, userAgent,
            oldValue: new { Status = oldStatus.ToString() }, newValue: new { Status = status.ToString() }, cancellationToken: cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);
        if (entity.Status == NotificationRuleStatus.Active)
        {
            throw new ConflictException("Aktif kural silinemez. Once duraklatin veya pasif yapin.");
        }

        var linkRepo = _unitOfWork.Repository<NotificationRuleTemplate>();
        var links = await linkRepo.GetQueryable(asNoTracking: false).Where(x => x.NotificationRuleId == id).ToListAsync(cancellationToken);
        linkRepo.RemoveRange(links);
        // Gecmis gonderimler korunur; NotificationRuleId alanini veritabani SET NULL yapar.
        _unitOfWork.Repository<NotificationRule>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "NOTIFICATION_RULE_DELETED", nameof(NotificationRule), id, ipAddress, userAgent,
            oldValue: Snapshot(entity, links.Select(x => x.NotificationTemplateId).ToList()), cancellationToken: cancellationToken);
    }

    private async Task<(TimeOnly LocalTime, List<long> TemplateIds)> ValidateAsync(AdminUpsertNotificationRuleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new AppValidationException(nameof(request.Name), "Ad zorunludur.");
        }

        if (!TimeOnly.TryParseExact(request.LocalTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var localTime))
        {
            throw new AppValidationException(nameof(request.LocalTime), "Saat HH:mm biciminde olmalidir.");
        }

        if (request.DaysOfWeekMask is < 1 or > 127)
        {
            throw new AppValidationException(nameof(request.DaysOfWeekMask), "En az bir gun secilmelidir.");
        }

        if (request.Kind == NotificationRuleKind.NutritionReminder && request.MealType is null)
        {
            throw new AppValidationException(nameof(request.MealType), "Beslenme hatirlaticisi icin ogun secilmelidir.");
        }

        var templateIds = request.TemplateIds.Distinct().ToList();
        if (templateIds.Count == 0)
        {
            return (localTime, templateIds);
        }

        var expectedType = NotificationContentRules.TypeFor(request.Kind);
        var templates = await _unitOfWork.Repository<NotificationTemplate>().GetQueryable()
            .Where(t => templateIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Type })
            .ToListAsync(cancellationToken);

        var missing = templateIds.Except(templates.Select(t => t.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new AppValidationException(nameof(request.TemplateIds), $"Sablon bulunamadi: {string.Join(", ", missing)}.");
        }

        var wrongType = templates.Where(t => t.Type != expectedType).Select(t => t.Id).ToList();
        if (wrongType.Count > 0)
        {
            throw new AppValidationException(nameof(request.TemplateIds), $"Bu kural yalnizca {expectedType} turundeki sablonlari kullanabilir (uyumsuz: {string.Join(", ", wrongType)}).");
        }

        return (localTime, templateIds);
    }

    private async Task EnsureHasActiveTemplateAsync(NotificationRuleKind kind, IReadOnlyCollection<long> templateIds, CancellationToken cancellationToken)
    {
        var type = NotificationContentRules.TypeFor(kind);
        var ids = templateIds.ToList();
        var hasActive = ids.Count > 0 && await _unitOfWork.Repository<NotificationTemplate>()
            .AnyAsync(t => ids.Contains(t.Id) && t.IsActive && t.Type == type, cancellationToken);
        if (!hasActive)
        {
            throw new ConflictException($"Kuralin aktif olabilmesi icin {type} turunde en az bir aktif sablon secilmelidir.");
        }
    }

    private async Task<Dictionary<long, List<(long TemplateId, bool IsActive)>>> GetTemplateLinksAsync(List<long> ruleIds, CancellationToken cancellationToken)
    {
        if (ruleIds.Count == 0)
        {
            return new Dictionary<long, List<(long, bool)>>();
        }

        var templates = _unitOfWork.Repository<NotificationTemplate>().GetQueryable();
        var links = await _unitOfWork.Repository<NotificationRuleTemplate>().GetQueryable()
            .Where(x => ruleIds.Contains(x.NotificationRuleId))
            .Select(x => new
            {
                x.NotificationRuleId,
                x.NotificationTemplateId,
                IsActive = templates.Any(t => t.Id == x.NotificationTemplateId && t.IsActive),
            })
            .ToListAsync(cancellationToken);

        return links.GroupBy(x => x.NotificationRuleId)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.NotificationTemplateId, x.IsActive)).OrderBy(x => x.NotificationTemplateId).ToList());
    }

    private static object Snapshot(NotificationRule entity, IReadOnlyCollection<long> templateIds) => new
    {
        entity.Name,
        Kind = entity.Kind.ToString(),
        Status = entity.Status.ToString(),
        LocalTime = entity.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        entity.DaysOfWeekMask,
        MealType = entity.MealType?.ToString(),
        entity.OnlyUsersWithPlan,
        TemplateIds = templateIds,
    };

    private async Task<NotificationRule> GetOrThrowAsync(long id, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<NotificationRule>().GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException(nameof(NotificationRule), id);

    private static AdminNotificationRuleDto MapToDto(NotificationRule entity, Dictionary<long, List<(long TemplateId, bool IsActive)>> links)
    {
        var ruleLinks = links.GetValueOrDefault(entity.Id) ?? new List<(long, bool)>();
        return new AdminNotificationRuleDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Kind = entity.Kind.ToString(),
            Type = NotificationContentRules.TypeFor(entity.Kind).ToString(),
            Status = entity.Status.ToString(),
            LocalTime = entity.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            DaysOfWeekMask = entity.DaysOfWeekMask,
            MealType = entity.MealType?.ToString(),
            OnlyUsersWithPlan = entity.OnlyUsersWithPlan,
            TemplateIds = ruleLinks.Select(x => x.TemplateId).ToList(),
            ActiveTemplateCount = ruleLinks.Count(x => x.IsActive),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }
}
