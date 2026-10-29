using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>MB Shop kategori yonetimi.</summary>
[Route("api/admin/store/categories")]
public class AdminStoreCategoryController : AdminControllerBase
{
    private readonly IAdminStoreCategoryService _service;

    public AdminStoreCategoryController(IAdminStoreCategoryService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminStoreCategoryDto>>>> GetList(
        [FromQuery] AdminStoreCategoryListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminStoreCategoryDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminStoreCategoryDto>>> Create(
        [FromBody] AdminUpsertStoreCategoryRequest request, CancellationToken cancellationToken)
        => CreatedResponse(await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminStoreCategoryDto>>> Update(
        long id, [FromBody] AdminUpsertStoreCategoryRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Aktif/pasif. Pasif kategorideki urunler mobilde gorunmez.</summary>
    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(long id, [FromBody] AdminUpdateStoreStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Yalnizca urunu ve alt kategorisi olmayan kategori silinebilir (aksi halde 409).</summary>
    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }
}
