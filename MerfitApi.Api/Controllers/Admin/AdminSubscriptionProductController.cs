using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.SubscriptionProducts;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Abonelik urunu (SubscriptionProduct) yonetimi uc noktalari (madde 16/19).</summary>
[Route("api/admin/subscription-products")]
public class AdminSubscriptionProductController : AdminControllerBase
{
    private readonly IAdminSubscriptionProductService _service;

    public AdminSubscriptionProductController(IAdminSubscriptionProductService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminSubscriptionProductDto>>>> GetList(
        [FromQuery] AdminSubscriptionProductListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminSubscriptionProductDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminSubscriptionProductDto>>> Create(
        [FromBody] AdminUpsertSubscriptionProductRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminSubscriptionProductDto>>> Update(
        long id, [FromBody] AdminUpsertSubscriptionProductRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long id, [FromBody] AdminUpdateSubscriptionProductStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Urune tanimli ozellikleri (features) doner.</summary>
    [HttpGet("{id:long}/features")]
    public async Task<ActionResult<ApiResponse<List<AdminSubscriptionProductFeatureItemDto>>>> GetFeatures(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetFeaturesAsync(id, cancellationToken));

    /// <summary>Urunun ozellik listesini tamamen degistirir (replace-all).</summary>
    [HttpPut("{id:long}/features")]
    public async Task<ActionResult<ApiResponse<List<AdminSubscriptionProductFeatureItemDto>>>> SetFeatures(
        long id, [FromBody] AdminSetSubscriptionProductFeaturesRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.SetFeaturesAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));
}
