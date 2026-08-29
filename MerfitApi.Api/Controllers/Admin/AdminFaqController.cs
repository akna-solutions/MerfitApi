using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Faq;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>FAQ kategorisi ve FAQ yonetimi uc noktalari (madde 26).</summary>
[Route("api/admin")]
public class AdminFaqController : AdminControllerBase
{
    private readonly IAdminFaqService _service;

    public AdminFaqController(IAdminFaqService service)
    {
        _service = service;
    }

    [HttpGet("faq/categories")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminFaqCategoryDto>>>> GetCategories(
        [FromQuery] AdminFaqCategoryListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetCategoriesAsync(request, cancellationToken));

    [HttpGet("faq/categories/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminFaqCategoryDto>>> GetCategoryById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetCategoryByIdAsync(id, cancellationToken));

    [HttpPost("faq/categories")]
    public async Task<ActionResult<ApiResponse<AdminFaqCategoryDto>>> CreateCategory(
        [FromBody] AdminUpsertFaqCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateCategoryAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("faq/categories/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminFaqCategoryDto>>> UpdateCategory(
        long id, [FromBody] AdminUpsertFaqCategoryRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateCategoryAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("faq/categories/{id:long}")]
    public async Task<ActionResult<ApiResponse>> DeleteCategory(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteCategoryAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpGet("faqs")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminFaqDto>>>> GetFaqs(
        [FromQuery] AdminFaqListRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetFaqsAsync(request, cancellationToken));

    [HttpGet("faqs/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminFaqDto>>> GetFaqById(long id, CancellationToken cancellationToken)
        => SuccessResponse(await _service.GetFaqByIdAsync(id, cancellationToken));

    [HttpPost("faqs")]
    public async Task<ActionResult<ApiResponse<AdminFaqDto>>> CreateFaq(
        [FromBody] AdminUpsertFaqRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateFaqAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return CreatedResponse(result);
    }

    [HttpPut("faqs/{id:long}")]
    public async Task<ActionResult<ApiResponse<AdminFaqDto>>> UpdateFaq(
        long id, [FromBody] AdminUpsertFaqRequest request, CancellationToken cancellationToken)
        => SuccessResponse(await _service.UpdateFaqAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken));

    [HttpDelete("faqs/{id:long}")]
    public async Task<ActionResult<ApiResponse>> DeleteFaq(long id, CancellationToken cancellationToken)
    {
        await _service.DeleteFaqAsync(id, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    [HttpPatch("faqs/{id:long}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateFaqStatus(long id, [FromBody] AdminUpdateFaqStatusRequest request, CancellationToken cancellationToken)
    {
        await _service.UpdateFaqStatusAsync(id, request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }

    /// <summary>Birden fazla SSS'nin siralamasini (SortOrder) tek istekte gunceller.</summary>
    [HttpPut("faqs/order")]
    public async Task<ActionResult<ApiResponse>> ReorderFaqs([FromBody] AdminReorderFaqsRequest request, CancellationToken cancellationToken)
    {
        await _service.ReorderFaqsAsync(request, CurrentAdminId, ClientIp, UserAgent, cancellationToken);
        return SuccessResponse();
    }
}
