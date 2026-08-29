using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Achievements;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Basarim (Achievement) yonetimi servis sozlesmesi (madde 21).</summary>
public interface IAdminAchievementService
{
    Task<PagedResult<AdminAchievementDto>> GetListAsync(AdminAchievementListRequest request, CancellationToken cancellationToken = default);

    Task<AdminAchievementDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminAchievementDto> CreateAsync(AdminUpsertAchievementRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminAchievementDto> UpdateAsync(long id, AdminUpsertAchievementRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(long id, AdminUpdateAchievementStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminAchievementUserListItemDto>> GetUsersAsync(long id, PagedRequest request, CancellationToken cancellationToken = default);
}
