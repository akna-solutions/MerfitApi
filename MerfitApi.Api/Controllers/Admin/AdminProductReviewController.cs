using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>
/// MB Shop urun yorumlari moderasyonu: liste/arama/filtre, detay (raporlar ve islem gecmisi), gizleme, yeniden
/// yayinlama, silme, raporlari kapatma. AdminOnly politikasi AdminControllerBase'den gelir.
/// </summary>
[Route("api/admin/store/reviews")]
public class AdminProductReviewController : AdminControllerBase
{
    private readonly IAdminProductReviewService _service;

    public AdminProductReviewController(IAdminProductReviewService service)
    {
        _service = service;
    }

    /// <summary>Filtreler: search, productId, userId, rating, status, reportedOnly, verifiedPurchase; sortBy: createdAt | rating | helpful | reports.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminProductReviewListItemDto>>>> GetList(
        [FromQuery] AdminProductReviewListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetListAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminProductReviewDto>>> GetById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetByIdAsync(id, cancellationToken));

    /// <summary>{ status: "Hidden" | "Published", note } - gizle / yeniden yayinla. Gecersiz gecis 409.</summary>
    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<ApiResponse<AdminProductReviewDto>>> UpdateStatus(
        long id, [FromBody] AdminUpdateProductReviewStatusRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Yorumu siler (Deleted; geri alinamaz, kayit ve gecmis korunur).</summary>
    [HttpPost("{id:long}/delete")]
    public async Task<ActionResult<ApiResponse<AdminProductReviewDto>>> Delete(
        long id, [FromBody] AdminProductReviewNoteRequest? request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.DeleteAsync(id, request?.Note, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Acik raporlari kapatir (Dismissed); yorum yayinda kalir.</summary>
    [HttpPost("{id:long}/reports/dismiss")]
    public async Task<ActionResult<ApiResponse<AdminProductReviewDto>>> DismissReports(
        long id, [FromBody] AdminProductReviewNoteRequest? request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.DismissReportsAsync(id, request?.Note, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    /// <summary>Yorum fotografi (yorum gizli veya silinmis olsa da admin gorebilir).</summary>
    [HttpGet("{id:long}/images/{imageId:long}")]
    [Produces("image/jpeg", "image/png", "image/webp")]
    public async Task<IActionResult> GetImage(long id, long imageId, CancellationToken cancellationToken)
    {
        var file = await _service.GetImageAsync(id, imageId, cancellationToken);
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType);
    }
}
