using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.SubscriptionTransactions;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminSubscriptionTransactionService'in varsayilan implementasyonu. Tamamen salt-okunurdur.</summary>
public class AdminSubscriptionTransactionService : IAdminSubscriptionTransactionService
{
    private readonly IUnitOfWork _unitOfWork;

    public AdminSubscriptionTransactionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<AdminSubscriptionTransactionListItemDto>> GetListAsync(AdminSubscriptionTransactionListRequest request, CancellationToken cancellationToken = default)
    {
        var transactions = _unitOfWork.Repository<SubscriptionTransaction>().GetQueryable();
        var subscriptions = _unitOfWork.Repository<Subscription>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        var query = from t in transactions
                     join s in subscriptions on t.SubscriptionId equals s.Id
                     select new { Transaction = t, Subscription = s };

        if (request.Provider.HasValue) query = query.Where(x => x.Subscription.Provider == request.Provider.Value);
        if (!string.IsNullOrWhiteSpace(request.ProductId)) query = query.Where(x => x.Transaction.ProductId == request.ProductId);
        if (request.UserId.HasValue) query = query.Where(x => x.Subscription.UserId == request.UserId.Value);
        if (request.From.HasValue) query = query.Where(x => x.Transaction.PurchasedAt >= request.From.Value);
        if (request.To.HasValue) query = query.Where(x => x.Transaction.PurchasedAt <= request.To.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);

        var raw = await query
            .OrderByDescending(x => x.Transaction.PurchasedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Transaction.Id,
                x.Transaction.SubscriptionId,
                x.Subscription.UserId,
                x.Transaction.TransactionId,
                x.Transaction.ProductId,
                x.Subscription.Provider,
                x.Transaction.Amount,
                x.Transaction.Currency,
                x.Transaction.PurchasedAt,
                x.Transaction.ExpiresAt,
            })
            .ToListAsync(cancellationToken);

        var userIds = raw.Select(x => x.UserId).Distinct().ToList();
        var userEmails = await users.Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.Email }).ToListAsync(cancellationToken);

        var items = raw.Select(x => new AdminSubscriptionTransactionListItemDto
        {
            Id = x.Id,
            SubscriptionId = x.SubscriptionId,
            UserId = x.UserId,
            UserEmail = userEmails.FirstOrDefault(u => u.Id == x.UserId)?.Email ?? "-",
            TransactionId = x.TransactionId,
            ProductId = x.ProductId,
            Provider = x.Provider.ToString(),
            Amount = x.Amount,
            Currency = x.Currency,
            PurchasedAt = x.PurchasedAt,
            ExpiresAt = x.ExpiresAt,
        }).ToList();

        return PagedResult<AdminSubscriptionTransactionListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminSubscriptionTransactionDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var transaction = await _unitOfWork.Repository<SubscriptionTransaction>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(SubscriptionTransaction), id);

        var subscription = await _unitOfWork.Repository<Subscription>().GetByIdAsync(transaction.SubscriptionId, cancellationToken)
            ?? throw new NotFoundException(nameof(Subscription), transaction.SubscriptionId);

        var userEmail = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == subscription.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        return new AdminSubscriptionTransactionDetailDto
        {
            Id = transaction.Id,
            SubscriptionId = transaction.SubscriptionId,
            UserId = subscription.UserId,
            UserEmail = userEmail ?? "-",
            TransactionId = transaction.TransactionId,
            ProductId = transaction.ProductId,
            Provider = subscription.Provider.ToString(),
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            PurchasedAt = transaction.PurchasedAt,
            ExpiresAt = transaction.ExpiresAt,
            OriginalTransactionId = transaction.OriginalTransactionId,
            HasRawReceipt = !string.IsNullOrEmpty(transaction.RawReceipt),
        };
    }
}
