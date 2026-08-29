using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Ai;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>AI istegi/sonucu incelemesi ve istatistikleri icin servis sozlesmesi (madde 31). Tamamen salt-okunurdur.</summary>
public interface IAdminAiService
{
    Task<PagedResult<AdminAiRequestListItemDto>> GetRequestsAsync(AdminAiRequestListRequest request, CancellationToken cancellationToken = default);

    Task<AdminAiRequestDetailDto> GetRequestByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminAiResultListItemDto>> GetResultsAsync(AdminAiResultListRequest request, CancellationToken cancellationToken = default);

    Task<AdminAiResultDetailDto> GetResultByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminAiStatisticsDto> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
