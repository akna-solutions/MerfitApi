using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.MuscleGroups;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Kas grubu (katalog) yonetimi servis sozlesmesi (madde 9). Global katalog verisi oldugundan tam CRUD icerir.</summary>
public interface IAdminMuscleGroupService
{
    Task<PagedResult<AdminMuscleGroupDto>> GetListAsync(AdminMuscleGroupListRequest request, CancellationToken cancellationToken = default);

    Task<AdminMuscleGroupDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminMuscleGroupDto> CreateAsync(AdminUpsertMuscleGroupRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminMuscleGroupDto> UpdateAsync(long id, AdminUpsertMuscleGroupRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
