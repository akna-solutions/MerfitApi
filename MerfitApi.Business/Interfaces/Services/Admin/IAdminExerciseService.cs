using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Exercises;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Egzersiz (katalog) yonetimi servis sozlesmesi (madde 8).</summary>
public interface IAdminExerciseService
{
    Task<PagedResult<AdminExerciseListItemDto>> GetListAsync(AdminExerciseListRequest request, CancellationToken cancellationToken = default);

    Task<AdminExerciseDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminExerciseDetailDto> CreateAsync(AdminUpsertExerciseRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminExerciseDetailDto> UpdateAsync(long id, AdminUpsertExerciseRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(long id, AdminUpdateExerciseStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
