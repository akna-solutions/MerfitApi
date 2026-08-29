using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Foods;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Besin (Food) katalog yonetimi servis sozlesmesi (madde 14). Global katalog verisi oldugundan tam CRUD icerir.</summary>
public interface IAdminFoodService
{
    Task<PagedResult<AdminFoodListItemDto>> GetListAsync(AdminFoodListRequest request, CancellationToken cancellationToken = default);

    Task<AdminFoodDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminFoodDetailDto> CreateAsync(AdminUpsertFoodRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminFoodDetailDto> UpdateAsync(long id, AdminUpsertFoodRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
