using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>MB Shop urun yonetimi: fiyat, indirim, SKU, gorseller, varyantlar ve stok.</summary>
[Route("api/admin/store/products")]
public class AdminProductController : AdminControllerBase
{
    private readonly IAdminProductService _service;

    public AdminProductController(IAdminProductService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminProductListItemDto>>>> GetList(
        [FromQuery] AdminProductListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminProductDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    /// <summary>Yeni urun. SKU veya slug kullaniliyorsa 409.</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminProductDto>>> Create(
        [FromBody] AdminUpsertProductRequest request, CancellationToken cancellationToken)
        => CreatedResponse(await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Urun bilgilerini gunceller. Stok bu istekle degismez; /stock kullanilir.</summary>
    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminProductDto>>> Update(
        long id, [FromBody] AdminUpsertProductRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long id, [FromBody] AdminUpdateStoreStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Stoga ekler/dusurur (delta). Stok 0'in altina inecekse 409.</summary>
    [HttpPatch("{id:long}/stock")]
    public async Task<ActionResult<ApiResponse<AdminProductDto>>> AdjustStock(
        long id, [FromBody] AdminAdjustStockRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.AdjustStockAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Siparislerde yer almis urun silinemez (409); pasife alinmalidir.</summary>
    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }
}
