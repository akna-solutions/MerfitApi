using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Translations;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Ceviri (Translation) yonetimi uc noktalari (madde 29).</summary>
[Route("api/admin/translations")]
public class AdminTranslationController : AdminControllerBase
{
    private readonly IAdminTranslationService _service;

    public AdminTranslationController(IAdminTranslationService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminTranslationDto>>>> GetList(
        [FromQuery] AdminTranslationListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminTranslationDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminTranslationDto>>> Create(
        [FromBody] AdminUpsertTranslationRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminTranslationDto>>> Update(
        long id, [FromBody] AdminUpsertTranslationRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Tek bir dil icin toplu ceviri aktarimi yapar (upsert, tek transaction).</summary>
    [HttpPost("import")]
    public async Task<ActionResult<ApiResponse<AdminTranslationBulkResultDto>>> Import(
        [FromBody] AdminImportTranslationsRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.ImportAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Birden fazla dil/anahtar kombinasyonunu tek istekte gunceller (upsert, tek transaction).</summary>
    [HttpPut("bulk")]
    public async Task<ActionResult<ApiResponse<AdminTranslationBulkResultDto>>> BulkUpdate(
        [FromBody] AdminBulkUpdateTranslationsRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.BulkUpdateAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));
}
