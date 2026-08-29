using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Faq;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>FAQ kategorisi ve FAQ yonetimi servis sozlesmesi (madde 26).</summary>
public interface IAdminFaqService
{
    Task<PagedResult<AdminFaqCategoryDto>> GetCategoriesAsync(AdminFaqCategoryListRequest request, CancellationToken cancellationToken = default);

    Task<AdminFaqCategoryDto> GetCategoryByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminFaqCategoryDto> CreateCategoryAsync(AdminUpsertFaqCategoryRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminFaqCategoryDto> UpdateCategoryAsync(long id, AdminUpsertFaqCategoryRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteCategoryAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminFaqDto>> GetFaqsAsync(AdminFaqListRequest request, CancellationToken cancellationToken = default);

    Task<AdminFaqDto> GetFaqByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminFaqDto> CreateFaqAsync(AdminUpsertFaqRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<AdminFaqDto> UpdateFaqAsync(long id, AdminUpsertFaqRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteFaqAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task UpdateFaqStatusAsync(long id, AdminUpdateFaqStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task ReorderFaqsAsync(AdminReorderFaqsRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}
