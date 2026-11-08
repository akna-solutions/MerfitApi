using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.WorkoutLab;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>
/// Antrenman Laboratuvari: onboarding tercihleriyle gercek antrenman programi algoritmasini test etme, sonuclari
/// degerlendirme ve karsilastirma. Program MerfitCustomerApi'deki gercek WorkoutPlanGenerator ile uretilir.
/// </summary>
public interface IAdminWorkoutLabService
{
    Task<AdminWorkoutLabOptionsDto> GetOptionsAsync(CancellationToken cancellationToken = default);

    Task<AdminWorkoutLabSimulationDetailDto> CreateAsync(AdminCreateWorkoutLabSimulationRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminWorkoutLabSimulationDetailDto> RerunAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminWorkoutLabSimulationListItemDto>> GetListAsync(AdminWorkoutLabSimulationListRequest request, CancellationToken cancellationToken = default);

    Task<AdminWorkoutLabSimulationDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminWorkoutLabSimulationDetailDto> EvaluateAsync(long id, AdminEvaluateWorkoutLabSimulationRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminWorkoutLabComparisonDto> CompareAsync(long leftId, long rightId, CancellationToken cancellationToken = default);
}
