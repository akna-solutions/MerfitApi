using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Content;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>
/// IAdminCampaignService'in varsayilan implementasyonu. Kampanyalar mevcut AppContent tablosunda Type = "Campaign"
/// olarak tutulur (genel icerik ekrani da ayni kayitlari gorur); bu servis yalnizca o ture dokunur.
/// </summary>
public class AdminCampaignService : IAdminCampaignService
{
    public const string CampaignType = "Campaign";

    public const string StatusLive = "Live";
    public const string StatusScheduled = "Scheduled";
    public const string StatusEnded = "Ended";
    public const string StatusPassive = "Passive";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;
    private readonly TimeProvider _timeProvider;

    public AdminCampaignService(IUnitOfWork unitOfWork, IAuditLogService auditLogService, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<AdminCampaignDto>> GetListAsync(AdminCampaignListRequest request, CancellationToken cancellationToken = default)
    {
        var now = UtcNow;
        var query = _unitOfWork.Repository<AppContent>().GetQueryable().Where(x => x.Type == CampaignType);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => (x.Title != null && x.Title.Contains(term))
                || (x.Category != null && x.Category.Contains(term))
                || x.Key.Contains(term));
        }

        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = request.Status.Trim() switch
            {
                StatusLive => query.Where(x => x.IsActive && (x.StartAt == null || x.StartAt <= now) && (x.EndAt == null || x.EndAt >= now)),
                StatusScheduled => query.Where(x => x.IsActive && x.StartAt != null && x.StartAt > now),
                StatusEnded => query.Where(x => x.IsActive && x.EndAt != null && x.EndAt < now),
                StatusPassive => query.Where(x => !x.IsActive),
                _ => throw new AppValidationException(nameof(request.Status), "Gecersiz durum filtresi."),
            };
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        var raw = await query
            .OrderBy(x => x.SortOrder)
            .ThenByDescending(x => x.StartAt ?? x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<AdminCampaignDto>.Create(raw.Select(x => MapToDto(x, now)).ToList(), request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminCampaignDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => MapToDto(await GetOrThrowAsync(id, tracking: false, cancellationToken), UtcNow);

    public async Task<AdminCampaignDto> CreateAsync(AdminUpsertCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var values = Normalize(request);
        var repo = _unitOfWork.Repository<AppContent>();

        var key = await GenerateKeyAsync(cancellationToken);
        var entity = new AppContent
        {
            Key = key,
            Type = CampaignType,
            CreatedAt = UtcNow,
            CreatedUser = adminUserId,
        };
        Apply(entity, values);

        await repo.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "CAMPAIGN_CREATED", nameof(AppContent), entity.Id, ipAddress, userAgent,
            newValue: AuditSnapshot(entity), cancellationToken: cancellationToken);

        return MapToDto(entity, UtcNow);
    }

    public async Task<AdminCampaignDto> UpdateAsync(long id, AdminUpsertCampaignRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var values = Normalize(request);
        var entity = await GetOrThrowAsync(id, tracking: true, cancellationToken);
        var oldValue = AuditSnapshot(entity);

        Apply(entity, values);
        entity.UpdatedAt = UtcNow;
        entity.UpdatedUser = adminUserId;

        _unitOfWork.Repository<AppContent>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "CAMPAIGN_UPDATED", nameof(AppContent), id, ipAddress, userAgent,
            oldValue, AuditSnapshot(entity), cancellationToken);

        return MapToDto(entity, UtcNow);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, tracking: true, cancellationToken);
        var oldValue = AuditSnapshot(entity);

        _unitOfWork.Repository<AppContent>().Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "CAMPAIGN_DELETED", nameof(AppContent), id, ipAddress, userAgent,
            oldValue, cancellationToken: cancellationToken);
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateContentStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, tracking: true, cancellationToken);
        var oldValue = new { entity.IsActive };

        entity.IsActive = request.IsActive;
        entity.UpdatedAt = UtcNow;
        entity.UpdatedUser = adminUserId;

        _unitOfWork.Repository<AppContent>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "CAMPAIGN_STATUS_CHANGED", nameof(AppContent), id, ipAddress, userAgent,
            oldValue, new { entity.IsActive }, cancellationToken);
    }

    public async Task ReorderAsync(AdminReorderCampaignsRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0)
        {
            throw new AppValidationException(nameof(request.Items), "En az bir kampanya gonderilmelidir.");
        }

        if (request.Items.Select(i => i.Id).Distinct().Count() != request.Items.Count)
        {
            throw new AppValidationException(nameof(request.Items), "Ayni kampanya birden fazla kez gonderilemez.");
        }

        var ids = request.Items.Select(i => i.Id).ToList();
        var repo = _unitOfWork.Repository<AppContent>();
        var entities = await repo.GetQueryable(asNoTracking: false)
            .Where(x => ids.Contains(x.Id) && x.Type == CampaignType)
            .ToListAsync(cancellationToken);

        if (entities.Count != ids.Count)
        {
            throw new NotFoundException("Siralanacak kampanyalardan biri bulunamadi.");
        }

        var orders = request.Items.ToDictionary(i => i.Id, i => i.SortOrder);
        var now = UtcNow;
        foreach (var entity in entities)
        {
            entity.SortOrder = orders[entity.Id];
            entity.UpdatedAt = now;
            entity.UpdatedUser = adminUserId;
            repo.Update(entity);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "CAMPAIGN_REORDERED", nameof(AppContent), null, ipAddress, userAgent,
            newValue: request.Items.Select(i => new { i.Id, i.SortOrder }).ToList(), cancellationToken: cancellationToken);
    }

    /// <summary>Kaydin durumunu (yayinda / planli / bitti / pasif) hesaplar.</summary>
    public static string ResolveStatus(AppContent entity, DateTime nowUtc)
    {
        if (!entity.IsActive) return StatusPassive;
        if (entity.StartAt.HasValue && entity.StartAt.Value > nowUtc) return StatusScheduled;
        if (entity.EndAt.HasValue && entity.EndAt.Value < nowUtc) return StatusEnded;
        return StatusLive;
    }

    private sealed record CampaignValues(string Title, string? Summary, string? Body, string? ImageUrl, string? LinkUrl,
        string? Category, int SortOrder, bool IsActive, DateTime? StartAt, DateTime? EndAt);

    private static CampaignValues Normalize(AdminUpsertCampaignRequest request)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length < 2)
        {
            throw new AppValidationException(nameof(request.Title), "Baslik zorunludur.");
        }

        var startAt = ToUtc(request.StartAt);
        var endAt = ToUtc(request.EndAt);
        if (startAt.HasValue && endAt.HasValue && endAt.Value <= startAt.Value)
        {
            throw new AppValidationException(nameof(request.EndAt), "Bitis tarihi baslangic tarihinden sonra olmalidir.");
        }

        return new CampaignValues(
            title,
            NullIfBlank(request.Summary),
            NullIfBlank(request.Body),
            ValidateHttpUrl(request.ImageUrl, nameof(request.ImageUrl), "Gorsel adresi http(s) ile baslayan gecerli bir URL olmalidir."),
            ValidateHttpUrl(request.LinkUrl, nameof(request.LinkUrl), "Baglanti http(s) ile baslayan gecerli bir URL olmalidir."),
            NullIfBlank(request.Category),
            request.SortOrder,
            request.IsActive,
            startAt,
            endAt);
    }

    private static void Apply(AppContent entity, CampaignValues values)
    {
        entity.Title = values.Title;
        entity.Description = values.Summary;
        entity.Body = values.Body;
        entity.ImageUrl = values.ImageUrl;
        entity.LinkUrl = values.LinkUrl;
        entity.Category = values.Category;
        entity.SortOrder = values.SortOrder;
        entity.IsActive = values.IsActive;
        entity.StartAt = values.StartAt;
        entity.EndAt = values.EndAt;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? ToUtc(DateTime? value) => value?.Kind switch
    {
        null => null,
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value!.Value, DateTimeKind.Utc),
    };

    private static string? ValidateHttpUrl(string? value, string field, string message)
    {
        var trimmed = NullIfBlank(value);
        if (trimmed is null) return null;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new AppValidationException(field, message);
        }

        return trimmed;
    }

    private async Task<string> GenerateKeyAsync(CancellationToken cancellationToken)
    {
        var repo = _unitOfWork.Repository<AppContent>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var key = $"campaign-{Guid.NewGuid():N}"[..21];
            if (!await repo.AnyAsync(x => x.Key == key, cancellationToken))
            {
                return key;
            }
        }

        throw new ConflictException("Kampanya anahtari olusturulamadi, lutfen tekrar deneyin.");
    }

    private async Task<AppContent> GetOrThrowAsync(long id, bool tracking, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<AppContent>().GetQueryable(asNoTracking: !tracking)
                .FirstOrDefaultAsync(x => x.Id == id && x.Type == CampaignType, cancellationToken)
            ?? throw new NotFoundException("Kampanya", id);
    }

    private static object AuditSnapshot(AppContent entity) => new
    {
        entity.Key, entity.Title, entity.ImageUrl, entity.LinkUrl, entity.Category, entity.SortOrder, entity.IsActive, entity.StartAt, entity.EndAt,
    };

    private static AdminCampaignDto MapToDto(AppContent entity, DateTime nowUtc) => new()
    {
        Id = entity.Id,
        Key = entity.Key,
        Title = entity.Title ?? string.Empty,
        Summary = entity.Description,
        Body = entity.Body,
        ImageUrl = entity.ImageUrl,
        LinkUrl = entity.LinkUrl,
        Category = entity.Category,
        SortOrder = entity.SortOrder,
        IsActive = entity.IsActive,
        StartAt = entity.StartAt,
        EndAt = entity.EndAt,
        Status = ResolveStatus(entity, nowUtc),
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };
}
