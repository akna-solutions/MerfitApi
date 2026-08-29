using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Scores;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Merfit Score genel bakis ve yeniden hesaplama uc noktalari (madde 22).</summary>
[Route("api/admin/scores")]
public class AdminScoreController : AdminControllerBase
{
    private readonly IAdminScoreService _service;

    public AdminScoreController(IAdminScoreService service)
    {
        _service = service;
    }

    /// <summary>Tum kullanicilarin guncel skorlarini (yuksekten dusuge, varsayilan) sayfali listeler.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminScoreListItemDto>>>> GetList(
        [FromQuery] AdminScoreListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    /// <summary>Belirli bir kullanicinin guncel skorunu doner.</summary>
    [HttpGet("{userId:long}")]
    public async Task<ActionResult<ApiResponse<AdminScoreListItemDto>>> GetByUserId(long userId, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByUserIdAsync(userId, cancellationToken));

    /// <summary>
    /// Kullanicinin skorunu yeniden hesaplar (bkz. AdminScoreService uzerindeki recalculate
    /// yorumu). Route, madde 22'deki "/api/admin/users/{userId}/score/recalculate" spesifikasyonuna
    /// uyacak sekilde mutlak (absolute) olarak tanimlanmistir.
    /// </summary>
    [HttpPost("/api/admin/users/{userId:long}/score/recalculate")]
    public async Task<ActionResult<ApiResponse<AdminRecalculateScoreResultDto>>> Recalculate(long userId, CancellationToken cancellationToken)
    {
        var result = await _service.RecalculateAsync(userId, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse(result);
    }
}
