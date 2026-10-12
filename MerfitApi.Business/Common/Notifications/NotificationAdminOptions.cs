namespace MerfitApi.Business.Common.Notifications;

/// <summary>
/// "Notifications" ayar bolumu (admin tarafi). Push gonderimi MerfitCustomerApi'deki gonderim motoru
/// tarafindan yapilir; bu API kampanya ve aylik odul bildirimlerini ortak NotificationDispatch kuyruguna yazar.
/// </summary>
public class NotificationAdminOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Kampanya / aylik odul isleyicisi (NotificationJobWorker) calissin mi.</summary>
    public bool WorkerEnabled { get; set; } = true;

    public TimeSpan WorkerInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Donem etiketleri icin varsayilan IANA saat dilimi (MerfitCustomerApi ile ayni).</summary>
    public string DefaultTimeZone { get; set; } = "Europe/Istanbul";

    /// <summary>Bu sayinin uzerindeki kitlelere gonderim acik onay (confirmLargeAudience) ister.</summary>
    public int LargeAudienceThreshold { get; set; } = 1000;

    /// <summary>Kampanya alicilari kuyruga bu boyutta partiler halinde yazilir.</summary>
    public int MaterializeBatchSize { get; set; } = 1000;

    public TimeSpan JobLockDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Kampanya / aylik odul isinin en fazla deneme sayisi; sonra Failed olur ve admin yeniden baslatabilir.</summary>
    public int MaxJobAttempts { get; set; } = 5;

    public TimeSpan JobRetryBaseDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>"Secili kullanicilar" kitlesinde izin verilen en fazla kullanici sayisi.</summary>
    public int MaxSelectedUsers { get; set; } = 5000;
}
