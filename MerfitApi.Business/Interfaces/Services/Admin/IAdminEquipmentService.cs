using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Equipment;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Ekipman (katalog) yonetimi servis sozlesmesi (madde 10). Global katalog verisi oldugundan tam CRUD icerir.</summary>
public interface IAdminEquipmentService
{
    Task<PagedResult<AdminEquipmentDto>> GetListAsync(AdminEquipmentListRequest request, CancellationToken cancellationToken = default);

    Task<AdminEquipmentDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminEquipmentDto> CreateAsync(AdminUpsertEquipmentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminEquipmentDto> UpdateAsync(long id, AdminUpsertEquipmentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
