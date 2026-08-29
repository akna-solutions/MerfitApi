using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Legal;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>Yasal belge (LegalDocument) yonetimi ve kullanici onaylarinin (UserConsent) incelenmesi uc noktalari (madde 30).</summary>
[Route("api/admin")]
public class AdminLegalController : AdminControllerBase
{
    private readonly IAdminLegalService _service;

    public AdminLegalController(IAdminLegalService service)
    {
        _service = service;
    }

    [HttpGet("legal-documents")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminLegalDocumentListItemDto>>>> GetDocuments(
        [FromQuery] AdminLegalDocumentListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetDocumentsAsync(request, cancellationToken));

    [HttpGet("legal-documents/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminLegalDocumentDetailDto>>> GetDocumentById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetDocumentByIdAsync(id, cancellationToken));

    [HttpPost("legal-documents")]
    public async Task<ActionResult<ApiResponse<AdminLegalDocumentDetailDto>>> CreateDocument(
        [FromBody] AdminUpsertLegalDocumentRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateDocumentAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("legal-documents/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminLegalDocumentDetailDto>>> UpdateDocument(
        long id, [FromBody] AdminUpsertLegalDocumentRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateDocumentAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("legal-documents/{id:long}")]
    public async Task<ActionResult<ApiResponse>> DeleteDocument(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteDocumentAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Belgeyi yayinlar; ayni tur+dile ait diger tum belgeleri otomatik olarak pasif hale getirir.</summary>
    [HttpPost("legal-documents/{id:long}/publish")]
    public async Task<ActionResult<ApiResponse<AdminLegalDocumentDetailDto>>> Publish(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.PublishDocumentAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Kullanici yasal metin onaylarini salt-okunur olarak listeler. Bu veriler admin tarafindan degistirilemez.</summary>
    [HttpGet("user-consents")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminConsentListItemDto>>>> GetUserConsents(
        [FromQuery] AdminUserConsentListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetUserConsentsAsync(request, cancellationToken));
}