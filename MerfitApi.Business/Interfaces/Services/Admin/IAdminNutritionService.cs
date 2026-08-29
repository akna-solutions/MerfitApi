using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Nutrition;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>
/// Kullanicilara ait beslenme (ogun) verilerinin admin tarafindan salt-okunur incelenmesi
/// icin servis sozlesmesi (madde 15). Kullaniciya ait uretilen veriye gereksiz CRUD verilmez.
/// </summary>
public interface IAdminNutritionService
{
    Task<PagedResult<AdminMealListItemDto>> GetMealsAsync(AdminMealListRequest request, CancellationToken cancellationToken = default);

    Task<AdminMealDetailDto> GetMealByIdAsync(long id, CancellationToken cancellationToken = default);
}
