using MerfitApi.Business.Dtos.Admin.Dashboard;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>Admin panelinin ana gosterge paneli (dashboard) icin metrik/analitik servis sozlesmesi.</summary>
public interface IAdminDashboardService
{
    Task<AdminDashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<AdminUserGrowthDto> GetUserGrowthAsync(AdminDashboardDateRangeRequest request, CancellationToken cancellationToken = default);

    Task<AdminRevenueDto> GetRevenueAsync(AdminDashboardDateRangeRequest request, CancellationToken cancellationToken = default);

    Task<AdminDashboardSubscriptionsDto> GetSubscriptionsAsync(CancellationToken cancellationToken = default);

    Task<AdminDashboardActivityDto> GetActivityAsync(AdminDashboardDateRangeRequest request, CancellationToken cancellationToken = default);
}
