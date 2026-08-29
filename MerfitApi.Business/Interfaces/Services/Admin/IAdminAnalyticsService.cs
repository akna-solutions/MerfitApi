using MerfitApi.Business.Dtos.Admin.Analytics;

namespace MerfitApi.Business.Interfaces.Services.Admin;

/// <summary>
/// Uygulama genelinde analitik metrikler icin servis sozlesmesi (madde 33). Tamamen salt-okunurdur;
/// tum metrikler mevcut entity yapisindan turetilir (bkz. AdminAnalyticsService yorumlarindaki
/// basitlestirme varsayimlari - DAU/WAU/MAU, retention ve churn icin ozel bir olay/analitik tablosu yoktur).
/// </summary>
public interface IAdminAnalyticsService
{
    Task<AdminAnalyticsUsersDto> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<AdminAnalyticsWorkoutsDto> GetWorkoutsAsync(CancellationToken cancellationToken = default);

    Task<AdminAnalyticsNutritionDto> GetNutritionAsync(CancellationToken cancellationToken = default);

    Task<AdminAnalyticsSubscriptionsDto> GetSubscriptionsAsync(CancellationToken cancellationToken = default);

    Task<AdminAnalyticsRevenueDto> GetRevenueAsync(CancellationToken cancellationToken = default);

    Task<AdminAnalyticsRetentionDto> GetRetentionAsync(CancellationToken cancellationToken = default);

    Task<AdminAnalyticsEngagementDto> GetEngagementAsync(CancellationToken cancellationToken = default);
}
