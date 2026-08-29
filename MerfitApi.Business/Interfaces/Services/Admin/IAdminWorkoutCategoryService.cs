using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.WorkoutCategories;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Antrenman kategorisi (katalog) yonetimi servis sozlesmesi (madde 12).</summary>
public interface IAdminWorkoutCategoryService
{
    Task<PagedResult<AdminWorkoutCategoryDto>> GetListAsync(AdminWorkoutCategoryListRequest request, CancellationToken cancellationToken = default);

    Task<AdminWorkoutCategoryDto> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminWorkoutCategoryDto> CreateAsync(AdminUpsertWorkoutCategoryRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminWorkoutCategoryDto> UpdateAsync(long id, AdminUpsertWorkoutCategoryRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
