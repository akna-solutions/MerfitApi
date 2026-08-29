using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Legal;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Yasal belge (LegalDocument) yonetimi ve kullanici onaylarinin (UserConsent) incelenmesi servis sozlesmesi (madde 30).</summary>
public interface IAdminLegalService
{
    Task<PagedResult<AdminLegalDocumentListItemDto>> GetDocumentsAsync(AdminLegalDocumentListRequest request, CancellationToken cancellationToken = default);

    Task<AdminLegalDocumentDetailDto> GetDocumentByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminLegalDocumentDetailDto> CreateDocumentAsync(AdminUpsertLegalDocumentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminLegalDocumentDetailDto> UpdateDocumentAsync(long id, AdminUpsertLegalDocumentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>Belgeyi yayinlar (IsActive=true) ve ayni Type+Language'e sahip diger tum belgeleri pasif hale getirir.</summary>
    Task<AdminLegalDocumentDetailDto> PublishDocumentAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>Kullanici onaylarini (UserConsent) salt-okunur olarak listeler.</summary>
    Task<PagedResult<AdminConsentListItemDto>> GetUserConsentsAsync(AdminUserConsentListRequest request, CancellationToken cancellationToken = default);
}