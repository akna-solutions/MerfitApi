using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.WorkoutPlans;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>
/// Kullanici antrenman planlarinin admin tarafindan incelenmesi/durum yonetimi servis sozlesmesi (madde 13).
/// WorkoutPlan kullaniciya ait oldugundan (UserId) admin sadece durumunu degistirebilir ve gunlerini yonetebilir;
/// plan icerigini (Name/Goal) admin degistirmez.
/// </summary>
public interface IAdminWorkoutPlanService
{
    Task<PagedResult<AdminWorkoutPlanListItemDto>> GetListAsync(AdminWorkoutPlanListRequest request, CancellationToken cancellationToken = default);

    Task<AdminWorkoutPlanDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(long id, AdminUpdateWorkoutPlanStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<List<AdminWorkoutPlanDayItemDto>> GetDaysAsync(long id, CancellationToken cancellationToken = default);

    Task<List<AdminWorkoutPlanDayItemDto>> SetDaysAsync(long id, AdminSetWorkoutPlanDaysRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
