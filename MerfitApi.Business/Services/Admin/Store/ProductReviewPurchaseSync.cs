using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin.Store;

/// <summary>
/// Urun yorumlarindaki "Onayli alisveris" etiketinin siparis durumuyla senkron tutulmasi. Kural MerfitCustomerApi'deki
/// StoreReviewPurchaseRules ile birebir aynidir: kullanicinin urunu iceren, teslim edilmis (Delivered) ve odemesi
/// Failed / Cancelled / RefundPending / Refunded olmayan bir siparisi varsa onaylidir. Admin siparis veya odeme durumunu
/// degistirdiginde (orn. teslim edildi, iade edildi) o kullanicinin siparisteki urunlere ait yorumlari ayni
/// transaction icinde tek bir UPDATE ile yeniden hesaplanir.
/// </summary>
public static class ProductReviewPurchaseSync
{
    public static readonly StorePaymentStatus[] DisqualifyingPaymentStatuses =
    {
        StorePaymentStatus.Failed,
        StorePaymentStatus.Cancelled,
        StorePaymentStatus.RefundPending,
        StorePaymentStatus.Refunded,
    };

    public static Task<int> RefreshAsync(IUnitOfWork unitOfWork, long userId, IReadOnlyCollection<long> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0) return Task.FromResult(0);
        var orders = unitOfWork.Repository<StoreOrder>().GetQueryable()
            .Where(o => o.UserId == userId && o.Status == StoreOrderStatus.Delivered && !DisqualifyingPaymentStatuses.Contains(o.PaymentStatus));
        var items = unitOfWork.Repository<StoreOrderItem>().GetQueryable();
        var ids = productIds.ToList();
        return unitOfWork.Repository<ProductReview>().GetQueryable(asNoTracking: false)
            .Where(r => r.UserId == userId && ids.Contains(r.ProductId))
            .ExecuteUpdateAsync(s => s.SetProperty(
                r => r.IsVerifiedPurchase,
                r => orders.Any(o => items.Any(i => i.OrderId == o.Id && i.ProductId == r.ProductId))), cancellationToken);
    }
}
