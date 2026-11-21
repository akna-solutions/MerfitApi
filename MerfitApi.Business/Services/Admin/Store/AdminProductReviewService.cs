using System.Text.Json;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin.Store;

/// <summary>
/// MB Shop urun yorumu moderasyonu. Durum degisiklikleri kosullu UPDATE ile yapilir (iki admin ayni anda farkli sonuc
/// yazamaz; ikinci istek 409 alir). Gecisler: Published -> Hidden | Deleted, Hidden -> Published | Deleted; Deleted
/// son durumdur. Islemler audit log'a "ProductReview" varligi ile yazilir ve yorum gecmisinde gosterilir.
/// </summary>
public class AdminProductReviewService : IAdminProductReviewService
{
    public const string AuditEntity = nameof(ProductReview);
    public const string HiddenAction = "STORE_REVIEW_HIDDEN";
    public const string PublishedAction = "STORE_REVIEW_PUBLISHED";
    public const string DeletedAction = "STORE_REVIEW_DELETED";
    public const string ReportsDismissedAction = "STORE_REVIEW_REPORTS_DISMISSED";

    private const int MaxSearchLength = 100;

    private static readonly Dictionary<ProductReviewStatus, ProductReviewStatus[]> Transitions = new()
    {
        [ProductReviewStatus.Published] = new[] { ProductReviewStatus.Hidden, ProductReviewStatus.Deleted },
        [ProductReviewStatus.Hidden] = new[] { ProductReviewStatus.Published, ProductReviewStatus.Deleted },
        [ProductReviewStatus.Deleted] = Array.Empty<ProductReviewStatus>(),
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;
    private readonly TimeProvider _timeProvider;

    public AdminProductReviewService(IUnitOfWork unitOfWork, IAuditLogService auditLogService, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private IQueryable<ProductReview> Reviews(bool tracking = false) => _unitOfWork.Repository<ProductReview>().GetQueryable(asNoTracking: !tracking);

    public async Task<PagedResult<AdminProductReviewListItemDto>> GetListAsync(AdminProductReviewListRequest request, CancellationToken cancellationToken = default)
    {
        var query = Reviews();
        var reports = _unitOfWork.Repository<ProductReviewReport>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = AdminStoreInput.ParseEnum<ProductReviewStatus>(request.Status, nameof(request.Status), "Gecersiz yorum durumu filtresi.");
            query = query.Where(r => r.Status == status);
        }
        else
        {
            query = query.Where(r => r.Status != ProductReviewStatus.Deleted);
        }

        if (request.Rating is { } rating)
        {
            if (rating is < 1 or > 5) throw new AppValidationException(nameof(request.Rating), "Yildiz filtresi 1-5 arasinda olmalidir.");
            query = query.Where(r => r.Rating == rating);
        }

        if (request.ProductId.HasValue) query = query.Where(r => r.ProductId == request.ProductId.Value);
        if (request.UserId.HasValue) query = query.Where(r => r.UserId == request.UserId.Value);
        if (request.VerifiedPurchase.HasValue) query = query.Where(r => r.IsVerifiedPurchase == request.VerifiedPurchase.Value);
        if (request.ReportedOnly) query = query.Where(r => reports.Any(p => p.ReviewId == r.Id && p.Status == ProductReviewReportStatus.Open));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            if (term.Length > MaxSearchLength) term = term[..MaxSearchLength];
            var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable()
                .Where(u => u.Email.ToLower().Contains(term) || u.UserName.ToLower().Contains(term)).Select(u => u.Id);
            var profiles = _unitOfWork.Repository<UserProfile>().GetQueryable()
                .Where(p => p.Username.ToLower().Contains(term)).Select(p => p.UserId);
            var products = _unitOfWork.Repository<Product>().GetQueryable()
                .Where(p => p.Name.ToLower().Contains(term)).Select(p => p.Id);
            query = query.Where(r => r.Comment.ToLower().Contains(term)
                || (r.Title != null && r.Title.ToLower().Contains(term))
                || users.Contains(r.UserId) || profiles.Contains(r.UserId) || products.Contains(r.ProductId));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var desc = !string.Equals(request.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<ProductReview> ordered = request.SortBy?.Trim().ToLowerInvariant() switch
        {
            null or "" or "createdat" => desc ? query.OrderByDescending(r => r.CreatedAt) : query.OrderBy(r => r.CreatedAt),
            "rating" => desc ? query.OrderByDescending(r => r.Rating) : query.OrderBy(r => r.Rating),
            "helpful" or "helpfulcount" => desc ? query.OrderByDescending(r => r.HelpfulCount) : query.OrderBy(r => r.HelpfulCount),
            "reports" or "openreportcount" => desc
                ? query.OrderByDescending(r => reports.Count(p => p.ReviewId == r.Id && p.Status == ProductReviewReportStatus.Open))
                : query.OrderBy(r => reports.Count(p => p.ReviewId == r.Id && p.Status == ProductReviewReportStatus.Open)),
            _ => throw new AppValidationException(nameof(request.SortBy), "Gecersiz siralama alani."),
        };

        var rows = await ordered.ThenByDescending(r => r.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var items = await FillListAsync(rows, cancellationToken);
        return PagedResult<AdminProductReviewListItemDto>.Create(items, request.Page, request.PageSize, total);
    }

    public async Task<AdminProductReviewDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var review = await Reviews().FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Yorum bulunamadi.");
        var dto = new AdminProductReviewDto();
        Fill(dto, review, (await LoadListContextAsync(new List<ProductReview> { review }, cancellationToken)));
        dto.ModerationNote = review.ModerationNote;
        dto.ModeratedAt = Utc(review.ModeratedAt);
        dto.DeletedAt = Utc(review.DeletedAt);
        dto.AllowedStatusTransitions = Transitions[review.Status].Select(s => s.ToString()).ToList();

        dto.Images = await _unitOfWork.Repository<ProductReviewImage>().GetQueryable()
            .Where(i => i.ReviewId == id)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new AdminProductReviewImageDto { Id = i.Id, ContentType = i.ContentType, ByteSize = i.ByteSize, Width = i.Width, Height = i.Height })
            .ToListAsync(cancellationToken);

        var reports = await _unitOfWork.Repository<ProductReviewReport>().GetQueryable()
            .Where(r => r.ReviewId == id)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
        var audit = await _unitOfWork.Repository<AuditLog>().GetQueryable()
            .Where(a => a.Entity == AuditEntity && a.EntityId == id)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        var userIds = reports.Select(r => r.ReporterUserId).Concat(audit.Where(a => a.UserId.HasValue).Select(a => a.UserId!.Value)).Distinct().ToList();
        var emails = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        dto.Reports = reports.Select(r => new AdminProductReviewReportDto
        {
            Id = r.Id,
            ReporterUserId = r.ReporterUserId,
            ReporterEmail = emails.GetValueOrDefault(r.ReporterUserId),
            Reason = r.Reason.ToString(),
            Description = r.Description,
            Status = r.Status.ToString(),
            CreatedAt = Utc(r.CreatedAt),
            ResolvedAt = Utc(r.ResolvedAt),
        }).ToList();

        // Kullanici olaylari yorum kaydindan, admin islemleri audit log'dan.
        var history = new List<AdminProductReviewHistoryDto>
        {
            new() { Action = "CREATED", CreatedAt = Utc(review.CreatedAt), ToStatus = nameof(ProductReviewStatus.Published) },
        };
        if (review.EditedAt is { } edited) history.Add(new AdminProductReviewHistoryDto { Action = "EDITED_BY_USER", CreatedAt = Utc(edited) });
        if (review.DeletedAt is { } deleted && !audit.Any(a => a.Action == DeletedAction))
        {
            history.Add(new AdminProductReviewHistoryDto { Action = "DELETED_BY_USER", CreatedAt = Utc(deleted), ToStatus = nameof(ProductReviewStatus.Deleted) });
        }

        history.AddRange(audit.Select(a =>
        {
            var (from, to, note) = ReadAudit(a.MetadataJson);
            return new AdminProductReviewHistoryDto
            {
                Action = a.Action,
                AdminUserId = a.UserId,
                AdminEmail = a.UserId.HasValue ? emails.GetValueOrDefault(a.UserId.Value) : null,
                FromStatus = from,
                ToStatus = to,
                Note = note,
                CreatedAt = Utc(a.CreatedAt),
            };
        }));
        dto.History = history.OrderBy(h => h.CreatedAt).ToList();
        return dto;
    }

    public async Task<AdminProductReviewDto> UpdateStatusAsync(long id, AdminUpdateProductReviewStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var target = AdminStoreInput.ParseEnum<ProductReviewStatus>(request.Status, nameof(request.Status), "Gecersiz yorum durumu.");
        if (target == ProductReviewStatus.Deleted)
        {
            throw new AppValidationException(nameof(request.Status), "Silmek icin silme islemini kullanin.");
        }

        var note = Note(request.Note);
        var reportStatus = target == ProductReviewStatus.Hidden ? ProductReviewReportStatus.Resolved : ProductReviewReportStatus.Dismissed;
        var action = target == ProductReviewStatus.Hidden ? HiddenAction : PublishedAction;
        return await TransitionAsync(id, target, note, reportStatus, action, adminUserId, ipAddress, userAgent, cancellationToken);
    }

    public Task<AdminProductReviewDto> DeleteAsync(long id, string? note, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, ProductReviewStatus.Deleted, Note(note), ProductReviewReportStatus.Resolved, DeletedAction, adminUserId, ipAddress, userAgent, cancellationToken);

    public async Task<AdminProductReviewDto> DismissReportsAsync(long id, string? note, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var review = await Reviews().FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Yorum bulunamadi.");
        var count = await CloseOpenReportsAsync(id, ProductReviewReportStatus.Dismissed, adminUserId, cancellationToken);
        if (count == 0) throw new ConflictException("Bu yorumun acik raporu yok.");

        await _auditLogService.LogAsync(adminUserId, ReportsDismissedAction, AuditEntity, id, ipAddress, userAgent,
            new { Status = review.Status.ToString() }, new { Status = review.Status.ToString(), DismissedReports = count, note = Note(note) }, cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<AdminProductReviewImageFile> GetImageAsync(long reviewId, long imageId, CancellationToken cancellationToken = default) =>
        await _unitOfWork.Repository<ProductReviewImage>().GetQueryable()
            .Where(i => i.Id == imageId && i.ReviewId == reviewId)
            .Select(i => new AdminProductReviewImageFile(i.Content, i.ContentType))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Gorsel bulunamadi.");

    // ---------------- Yardimcilar ----------------

    private async Task<AdminProductReviewDto> TransitionAsync(long id, ProductReviewStatus target, string? note, ProductReviewReportStatus reportStatus,
        string action, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        var review = await Reviews().FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Yorum bulunamadi.");
        var current = review.Status;
        if (current == target) throw new ConflictException($"Yorum zaten '{target}' durumunda.");
        if (!Transitions[current].Contains(target)) throw new ConflictException($"Yorum '{current}' durumundan '{target}' durumuna gecirilemez.");

        var now = UtcNow;
        int closedReports;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var deletedAt = target == ProductReviewStatus.Deleted ? now : review.DeletedAt;
            var claimed = await Reviews(tracking: true)
                .Where(r => r.Id == id && r.Status == current)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, target)
                    .SetProperty(r => r.ModerationNote, note)
                    .SetProperty(r => r.ModeratedAt, now)
                    .SetProperty(r => r.ModeratedByUserId, adminUserId)
                    .SetProperty(r => r.DeletedAt, deletedAt)
                    .SetProperty(r => r.UpdatedAt, now)
                    .SetProperty(r => r.UpdatedUser, adminUserId), cancellationToken);
            if (claimed == 0)
            {
                throw new ConflictException("Yorum ayni anda baska bir islemle degisti. Sayfayi yenileyip tekrar deneyin.");
            }

            closedReports = await CloseOpenReportsAsync(id, reportStatus, adminUserId, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _auditLogService.LogAsync(adminUserId, action, AuditEntity, id, ipAddress, userAgent,
            new { Status = current.ToString() }, new { Status = target.ToString(), note, ClosedReports = closedReports }, cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    private Task<int> CloseOpenReportsAsync(long reviewId, ProductReviewReportStatus status, long? adminUserId, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        return _unitOfWork.Repository<ProductReviewReport>().GetQueryable(asNoTracking: false)
            .Where(r => r.ReviewId == reviewId && r.Status == ProductReviewReportStatus.Open)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.ResolvedAt, now)
                .SetProperty(r => r.ResolvedByUserId, adminUserId)
                .SetProperty(r => r.UpdatedAt, now)
                .SetProperty(r => r.UpdatedUser, adminUserId), cancellationToken);
    }

    private sealed record ListContext(
        Dictionary<long, string> Products,
        Dictionary<long, (string Email, string? Username)> Users,
        Dictionary<long, int> Images,
        Dictionary<long, (int Open, int Total)> Reports);

    private async Task<ListContext> LoadListContextAsync(List<ProductReview> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var userIds = rows.Select(r => r.UserId).Distinct().ToList();

        var products = await _unitOfWork.Repository<Product>().GetQueryable()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var emails = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(cancellationToken);
        var usernames = await _unitOfWork.Repository<UserProfile>().GetQueryable()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.Username })
            .ToListAsync(cancellationToken);
        var usernameByUser = usernames.GroupBy(u => u.UserId).ToDictionary(g => g.Key, g => (string?)g.First().Username);
        var users = emails.ToDictionary(u => u.Id, u => (u.Email, usernameByUser.GetValueOrDefault(u.Id)));

        var images = await _unitOfWork.Repository<ProductReviewImage>().GetQueryable()
            .Where(i => ids.Contains(i.ReviewId))
            .GroupBy(i => i.ReviewId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var reports = await _unitOfWork.Repository<ProductReviewReport>().GetQueryable()
            .Where(r => ids.Contains(r.ReviewId))
            .GroupBy(r => r.ReviewId)
            .Select(g => new { g.Key, Open = g.Count(r => r.Status == ProductReviewReportStatus.Open), Total = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => (x.Open, x.Total), cancellationToken);
        return new ListContext(products, users, images, reports);
    }

    private async Task<List<AdminProductReviewListItemDto>> FillListAsync(List<ProductReview> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return new List<AdminProductReviewListItemDto>();
        var context = await LoadListContextAsync(rows, cancellationToken);
        return rows.Select(r => Fill(new AdminProductReviewListItemDto(), r, context)).ToList();
    }

    private static T Fill<T>(T dto, ProductReview r, ListContext context) where T : AdminProductReviewListItemDto
    {
        var user = context.Users.GetValueOrDefault(r.UserId);
        var reports = context.Reports.GetValueOrDefault(r.Id);
        dto.Id = r.Id;
        dto.ProductId = r.ProductId;
        dto.ProductName = context.Products.GetValueOrDefault(r.ProductId) ?? $"#{r.ProductId}";
        dto.UserId = r.UserId;
        dto.UserEmail = user.Email;
        dto.UserName = user.Username;
        dto.Rating = r.Rating;
        dto.Title = r.Title;
        dto.Comment = r.Comment;
        dto.IsVerifiedPurchase = r.IsVerifiedPurchase;
        dto.Status = r.Status.ToString();
        dto.HelpfulCount = r.HelpfulCount;
        dto.NotHelpfulCount = r.NotHelpfulCount;
        dto.ImageCount = context.Images.GetValueOrDefault(r.Id);
        dto.OpenReportCount = reports.Open;
        dto.ReportCount = reports.Total;
        dto.CreatedAt = Utc(r.CreatedAt);
        dto.EditedAt = Utc(r.EditedAt);
        return dto;
    }

    private static (string? From, string? To, string? Note) ReadAudit(JsonDocument? metadata)
    {
        if (metadata is null) return (null, null, null);
        var root = metadata.RootElement;
        static string? Read(JsonElement element, string parent, string name) =>
            element.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        return (Read(root, "oldValue", "Status"), Read(root, "newValue", "Status"), Read(root, "newValue", "note"));
    }

    private static string? Note(string? value)
    {
        var note = AdminStoreInput.NullIfBlank(value);
        return note is { Length: > 500 } ? note[..500] : note;
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}
