using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.SubscriptionTransactions;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>
/// Abonelik odeme islemlerinin (SubscriptionTransaction) admin tarafindan salt-okunur incelenmesi
/// icin servis sozlesmesi (madde 18). Hassas veriler (RawReceipt) asla listede/detayda ham olarak donmez.
/// </summary>
public interface IAdminSubscriptionTransactionService
{
    Task<PagedResult<AdminSubscriptionTransactionListItemDto>> GetListAsync(AdminSubscriptionTransactionListRequest request, CancellationToken cancellationToken = default);

    Task<AdminSubscriptionTransactionDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);
}
