namespace Merfit.Application.Common.Interfaces;

/// <summary>
/// Cache abstraction the Application layer codes against. The MVP implementation is in-memory
/// (Infrastructure/Caching/MemoryCacheService); swapping in a RedisCacheService later requires no
/// Application-layer changes.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
