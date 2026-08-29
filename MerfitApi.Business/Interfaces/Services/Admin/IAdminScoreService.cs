using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Scores;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>
/// Merfit Score genel bakis ve yeniden hesaplama servis sozlesmesi (madde 22).
/// Kullaniciya ozel skor gecmisi/kirilimi zaten AdminUserController altinda mevcuttur;
/// bu servis tum kullanicilar genelinde bir "skor tablosu" gorunumu ve elle recalculate saglar.
/// </summary>
public interface IAdminScoreService
{
    Task<PagedResult<AdminScoreListItemDto>> GetListAsync(AdminScoreListRequest request, CancellationToken cancellationToken = default);

    Task<AdminScoreListItemDto> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default);

    Task<AdminRecalculateScoreResultDto> RecalculateAsync(long userId, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
