using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Languages;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Dil (Language) yonetimi servis sozlesmesi (madde 28).</summary>
public interface IAdminLanguageService
{
    Task<PagedResult<AdminLanguageDto>> GetListAsync(AdminLanguageListRequest request, CancellationToken cancellationToken = default);

    Task<AdminLanguageDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminLanguageDto> CreateAsync(AdminUpsertLanguageRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminLanguageDto> UpdateAsync(long id, AdminUpsertLanguageRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
