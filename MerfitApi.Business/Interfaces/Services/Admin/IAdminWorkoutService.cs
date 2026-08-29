using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Workouts;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>
/// Antrenman (Workout) yonetimi servis sozlesmesi (madde 11). Workout ile iliskili
/// egzersiz/ekipman listeleri, ayri CRUD uc noktalari yerine aggregate (tam degistirme)
/// mantigiyla yonetilir (bkz. madde 11 - "aggregate mantigini kullan").
/// </summary>
public interface IAdminWorkoutService
{
    Task<PagedResult<AdminWorkoutListItemDto>> GetListAsync(AdminWorkoutListRequest request, CancellationToken cancellationToken = default);

    Task<AdminWorkoutDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminWorkoutDetailDto> CreateAsync(AdminUpsertWorkoutRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminWorkoutDetailDto> UpdateAsync(long id, AdminUpsertWorkoutRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(long id, AdminUpdateWorkoutStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task UpdateFeaturedAsync(long id, AdminUpdateWorkoutFeaturedRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task UpdatePremiumAsync(long id, AdminUpdateWorkoutPremiumRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<List<AdminWorkoutExerciseItemDto>> GetExercisesAsync(long workoutId, CancellationToken cancellationToken = default);

    Task<List<AdminWorkoutExerciseItemDto>> SetExercisesAsync(long workoutId, AdminSetWorkoutExercisesRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<List<AdminWorkoutEquipmentItemDto>> GetEquipmentAsync(long workoutId, CancellationToken cancellationToken = default);

    Task<List<AdminWorkoutEquipmentItemDto>> SetEquipmentAsync(long workoutId, AdminSetWorkoutEquipmentRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
