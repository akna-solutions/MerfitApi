namespace Merfit.Application.Common.Interfaces;

public sealed record UploadedFile(string PublicUrl, string StorageKey);

/// <summary>MVP implementation stores to local disk under wwwroot/uploads; the interface is provider-agnostic so Azure Blob / S3 / R2 can be swapped in later.</summary>
public interface IFileStorageService
{
    Task<UploadedFile> UploadAsync(Stream content, string fileName, string contentType, string folder, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
