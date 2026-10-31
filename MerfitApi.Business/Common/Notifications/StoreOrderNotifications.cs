using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;

namespace MerfitApi.Business.Common.Notifications;

/// <summary>
/// MB Shop siparis/odeme durumu degistiginde musteriye gidecek bildirim (StoreOrderUpdate). Kayit, siparis
/// degisikligiyle ayni transaction'da gonderim kuyruguna (NotificationDispatch) yazilir; push'u MerfitCustomerApi
/// NotificationDispatchProcessor gonderir. Bildirim mobilde MB Shop gelen kutusuna ve "mbshop" Android kanalina duser,
/// MB Fit bildirimlerinden ayridir.
///
/// Siparis ve odeme gecisleri geri donussuz oldugu icin her durum bir sipariste en fazla bir kez olusur;
/// DedupeKey bu durumu icerir, ayni olay iki kez kuyruga yazilmaz.
/// </summary>
public static class StoreOrderNotifications
{
    /// <summary>Siparis bildirimi bu sureden sonra gonderilmez (ornegin uzun sessiz saat / servis kesintisi).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public static string DedupeKey(long orderId, string kind, string state) => $"store-order:{orderId}:{kind}:{state}";

    /// <summary>Siparis durumu degisikligi (iptal dahil). Received icin bildirim yoktur (musteri siparisi kendisi verdi).</summary>
    public static NotificationDispatch? ForStatusChange(StoreOrder order, DateTime nowUtc)
    {
        var no = order.OrderNumber;
        (string Title, string Body)? content = order.Status switch
        {
            StoreOrderStatus.Preparing => ("Siparişin hazırlanıyor", $"{no} numaralı siparişin hazırlanmaya başladı."),
            StoreOrderStatus.Shipped => ("Siparişin kargoya verildi", $"{no} numaralı siparişin yola çıktı. Durumunu Siparişlerim'den takip edebilirsin."),
            StoreOrderStatus.Delivered => ("Siparişin teslim edildi", $"{no} numaralı siparişin teslim edildi. İyi kullanmanı dileriz!"),
            StoreOrderStatus.Cancelled => ("Siparişin iptal edildi", CancelledBody(order)),
            _ => null,
        };

        return content is null ? null : Build(order, DedupeKey(order.Id, "status", order.Status.ToString()), content.Value, nowUtc);
    }

    /// <summary>
    /// Odeme durumu degisikligi. Odeme alinamadigi icin siparis iptal olduysa tek bir "iptal" bildirimi gider.
    /// Cancelled (siparis iptaliyle gelen) odeme durumu icin ayrica bildirim yoktur.
    /// </summary>
    public static NotificationDispatch? ForPaymentChange(StoreOrder order, bool orderCancelled, DateTime nowUtc)
    {
        var no = order.OrderNumber;
        if (orderCancelled)
        {
            return Build(order, DedupeKey(order.Id, "status", StoreOrderStatus.Cancelled.ToString()),
                ("Ödemen alınamadı", $"{no} numaralı siparişin ödemesi alınamadığı için iptal edildi."), nowUtc);
        }

        (string Title, string Body)? content = order.PaymentStatus switch
        {
            StorePaymentStatus.Paid => ("Ödemen alındı", $"{no} numaralı siparişin ödemesi onaylandı."),
            StorePaymentStatus.Failed => ("Ödemen alınamadı", $"{no} numaralı siparişin ödemesi alınamadı. Ayrıntılar için siparişine göz at."),
            StorePaymentStatus.RefundPending => ("İade süreci başladı", $"{no} numaralı siparişin için ücret iaden başlatıldı."),
            StorePaymentStatus.Refunded => ("Ücret iaden yapıldı", $"{no} numaralı siparişin için ücret iaden tamamlandı."),
            _ => null,
        };

        return content is null ? null : Build(order, DedupeKey(order.Id, "payment", order.PaymentStatus.ToString()), content.Value, nowUtc);
    }

    private static string CancelledBody(StoreOrder order) => order.PaymentStatus == StorePaymentStatus.RefundPending
        ? $"{order.OrderNumber} numaralı siparişin iptal edildi. Ücret iaden başlatıldı."
        : $"{order.OrderNumber} numaralı siparişin iptal edildi.";

    private static NotificationDispatch Build(StoreOrder order, string dedupeKey, (string Title, string Body) content, DateTime nowUtc) => new()
    {
        UserId = order.UserId,
        Type = NotificationType.StoreOrderUpdate,
        Source = NotificationDispatchSource.StoreOrder,
        DedupeKey = dedupeKey,
        Title = content.Title,
        Body = content.Body.Length > 500 ? content.Body[..500] : content.Body,
        DataJson = NotificationNavigation.Screen("storeOrder", ("orderId", order.Id.ToString())),
        ScheduledForUtc = nowUtc,
        ExpiresAtUtc = nowUtc + Lifetime,
        Status = NotificationDispatchStatus.Pending,
        NextAttemptAtUtc = nowUtc,
        CreatedAt = nowUtc,
    };
}
