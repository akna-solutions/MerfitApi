using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Translations;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Ceviri (Translation) yonetimi servis sozlesmesi (madde 29).</summary>
public interface IAdminTranslationService
{
    Task<PagedResult<AdminTranslationDto>> GetListAsync(AdminTranslationListRequest request, CancellationToken cancellationToken = default);

    Task<AdminTranslationDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminTranslationDto> CreateAsync(AdminUpsertTranslationRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminTranslationDto> UpdateAsync(long id, AdminUpsertTranslationRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>Tek bir dil icin toplu ceviri aktarimi (upsert) yapar; tek transaction icinde calisir.</summary>
    Task<AdminTranslationBulkResultDto> ImportAsync(AdminImportTranslationsRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>Birden fazla dil/anahtar kombinasyonu icin toplu guncelleme (upsert) yapar; tek transaction icinde calisir.</summary>
    Task<AdminTranslationBulkResultDto> BulkUpdateAsync(AdminBulkUpdateTranslationsRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
