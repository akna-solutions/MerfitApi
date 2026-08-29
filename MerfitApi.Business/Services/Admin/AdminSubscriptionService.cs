using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Subscriptions;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminSubscriptionService'in varsayilan implementasyonu.</summary>
public class AdminSubscriptionService : IAdminSubscriptionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminSubscriptionService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminSubscriptionListItemDto>> GetListAsync(AdminSubscriptionListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Subscription>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();
        var products = _unitOfWork.Repository<SubscriptionProduct>().GetQueryable();

        if (request.UserId.HasValue) query = query.Where(x => x.UserId == request.UserId.Value);
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
        if (request.Provider.HasValue) query = query.Where(x => x.Provider == request.Provider.Value);
        if (request.SubscriptionProductId.HasValue) query = query.Where(x => x.SubscriptionProductId == request.SubscriptionProductId.Value);
        if (request.StartedFrom.HasValue) query = query.Where(x => x.StartedAt >= request.StartedFrom.Value);
        if (request.StartedTo.HasValue) query = query.Where(x => x.StartedAt <= request.StartedTo.Value);
        if (request.ExpiresFrom.HasValue) query = query.Where(x => x.ExpiresAt != null && x.ExpiresAt >= request.ExpiresFrom.Value);
        if (request.ExpiresTo.HasValue) query = query.Where(x => x.ExpiresAt != null && x.ExpiresAt <= request.ExpiresTo.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);

        var raw = await query
            .OrderByDescending(x => x.StartedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var userIds = raw.Select(x => x.UserId).Distinct().ToList();
        var productIds = raw.Select(x => x.SubscriptionProductId).Distinct().ToList();

        var userEmails = await users.Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.Email }).ToListAsync(cancellationToken);
        var productNames = await products.Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);

        var items = raw.Select(x => new AdminSubscriptionListItemDto
        {
            Id = x.Id,
            UserId = x.UserId,
            UserEmail = userEmails.FirstOrDefault(u => u.Id == x.UserId)?.Email ?? "-",
            SubscriptionProductId = x.SubscriptionProductId,
            ProductName = productNames.FirstOrDefault(p => p.Id == x.SubscriptionProductId)?.Name ?? "-",
            Provider = x.Provider.ToString(),
            Status = x.Status.ToString(),
            StartedAt = x.StartedAt,
            ExpiresAt = x.ExpiresAt,
            AutoRenew = x.AutoRenew,
            CancelledAt = x.CancelledAt,
        }).ToList();

        return PagedResult<AdminSubscriptionListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminSubscriptionDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrThrowAsync(id, cancellationToken);

        var userEmail = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == entity.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        var productName = await _unitOfWork.Repository<SubscriptionProduct>().GetQueryable()
            .Where(p => p.Id == entity.SubscriptionProductId).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);

        return new AdminSubscriptionDetailDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            UserEmail = userEmail ?? "-",
            SubscriptionProductId = entity.SubscriptionProductId,
            ProductName = productName ?? "-",
            Provider = entity.Provider.ToString(),
            Status = entity.Status.ToString(),
            StartedAt = entity.StartedAt,
            ExpiresAt = entity.ExpiresAt,
            AutoRenew = entity.AutoRenew,
            CancelledAt = entity.CancelledAt,
            ExternalTransactionId = entity.ExternalTransactionId,
        };
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateSubscriptionStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Subscription>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Subscription), id);

        var oldValue = new { entity.Status };

        entity.Status = request.Status;
        if (request.Status is Domain.Entities.Enums.SubscriptionStatus.Cancelled or Domain.Entities.Enums.SubscriptionStatus.Refunded)
        {
            entity.CancelledAt ??= DateTime.UtcNow;
            entity.AutoRenew = false;
        }

        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "SUBSCRIPTION_STATUS_CHANGED", nameof(Subscription), id, ipAddress, userAgent,
            oldValue, new { entity.Status, request.Reason }, cancellationToken);
    }

    private async Task<Subscription> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Subscription>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Subscription), id);
    }
}
