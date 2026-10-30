namespace MerfitApi.Domain.Interfaces;

/// <summary>
/// Bir kullaniciyi ve ona bagli tum kayitlari veritabanindan fiziksel olarak siler (geri alinamaz).
/// Ortak "merfit" veritabaninda MerfitCustomerApi'nin tablolari da bulundugundan implementasyon
/// EF modelinin bilmedigi tablolari da kapsar; islem tek bir transaction icinde calisir.
/// </summary>
public interface IUserDataPurger
{
    /// <summary>
    /// Kullaniciya ait tum alt kayitlari ve kullanici satirini siler. Bilinmeyen bir tablo hala
    /// kullaniciya referans veriyorsa hicbir sey silinmez ve ConflictException firlatilir.
    /// </summary>
    Task PurgeAsync(long userId, CancellationToken cancellationToken = default);
}
