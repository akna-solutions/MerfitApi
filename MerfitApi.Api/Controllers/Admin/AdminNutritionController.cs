using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Nutrition;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>
/// Kullanicilara ait ogun (meal) verilerinin salt-okunur incelenmesi (madde 15).
/// Kullaniciya ozel diger beslenme uc noktalari (nutrition-goals, water-logs) AdminUserController altindadir.
/// </summary>
[Route("api/admin/meals")]
public class AdminNutritionController : AdminControllerBase
{
    private readonly IAdminNutritionService _service;

    public AdminNutritionController(IAdminNutritionService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminMealListItemDto>>>> GetList(
        [FromQuery] AdminMealListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetMealsAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminMealDetailDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetMealByIdAsync(id, cancellationToken));
}
