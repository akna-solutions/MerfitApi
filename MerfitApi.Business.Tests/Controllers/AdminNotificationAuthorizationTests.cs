using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MerfitApi.Api.BackgroundJobs;
using MerfitApi.Api.Controllers.Admin;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Business.Interfaces.Services;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.Controllers;

/// <summary>
/// Bildirim admin uc noktalarinin gercek pipeline'da (JWT + "AdminOnly" policy) korundugunu dogrular:
/// kimliksiz 401, "User" rolu 403, Admin/SuperAdmin 200. Servisler sahtedir; veritabani kullanilmaz.
/// </summary>
public class AdminNotificationAuthorizationTests : IClassFixture<WebApplicationFactory<AdminNotificationCampaignController>>
{
    private readonly WebApplicationFactory<AdminNotificationCampaignController> _factory;
    private readonly Mock<IAdminNotificationCampaignService> _campaigns = new();
    private readonly Mock<IAdminNotificationRuleService> _rules = new();
    private readonly Mock<IAdminNotificationTemplateService> _templates = new();
    private readonly Mock<IAdminNotificationDispatchService> _dispatches = new();
    private readonly Mock<IAdminMonthlyRewardNotificationService> _monthly = new();

    public AdminNotificationAuthorizationTests(WebApplicationFactory<AdminNotificationCampaignController> factory)
    {
        _campaigns.Setup(s => s.GetListAsync(It.IsAny<AdminNotificationCampaignListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<AdminNotificationCampaignListItemDto>.Create(Array.Empty<AdminNotificationCampaignListItemDto>(), 1, 20, 0));
        _campaigns.Setup(s => s.ScheduleAsync(It.IsAny<long>(), It.IsAny<AdminScheduleNotificationCampaignRequest>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminNotificationCampaignDetailDto { Id = 1, Status = "Scheduled" });
        _rules.Setup(s => s.GetListAsync(It.IsAny<AdminNotificationRuleListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<AdminNotificationRuleDto>.Create(Array.Empty<AdminNotificationRuleDto>(), 1, 20, 0));
        _templates.Setup(s => s.GetMetadata()).Returns(new AdminNotificationMetadataDto());
        _dispatches.Setup(s => s.GetListAsync(It.IsAny<AdminNotificationDispatchListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<AdminNotificationDispatchDto>.Create(Array.Empty<AdminNotificationDispatchDto>(), 1, 20, 0));
        _monthly.Setup(s => s.TriggerAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminMonthlyRewardNotificationRunDto { LeaderboardPeriodId = 3, Status = "Pending" });

        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationJobWorker)));
            services.AddScoped(_ => _campaigns.Object);
            services.AddScoped(_ => _rules.Object);
            services.AddScoped(_ => _templates.Object);
            services.AddScoped(_ => _dispatches.Object);
            services.AddScoped(_ => _monthly.Object);
        }));
    }

    private HttpClient CreateClient(UserRole? role)
    {
        var client = _factory.CreateClient();
        if (role is not null)
        {
            using var scope = _factory.Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var (token, _) = tokens.GenerateAccessToken(new ApplicationUser { Id = 5, Email = "x@test.local", Role = role.Value });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/admin/notification-campaigns" },
        { "POST", "/api/admin/notification-campaigns/1/schedule" },
        { "GET", "/api/admin/notification-rules" },
        { "GET", "/api/admin/notification-templates/metadata" },
        { "GET", "/api/admin/notification-dispatches" },
        { "POST", "/api/admin/notification-monthly-runs/periods/3/trigger" },
    };

    private static HttpRequestMessage Request(string method, string url) => new(new HttpMethod(method), url)
    {
        Content = method == "GET" ? null : JsonContent.Create(new { confirmLargeAudience = true }),
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Anonymous_Gets401(string method, string url)
    {
        var response = await CreateClient(null).SendAsync(Request(method, url));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task RegularUser_Gets403(string method, string url)
    {
        var response = await CreateClient(UserRole.User).SendAsync(Request(method, url));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _monthly.Verify(s => s.TriggerAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Admin_IsAllowed(string method, string url)
    {
        var response = await CreateClient(UserRole.Admin).SendAsync(Request(method, url));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LegacyDirectSendEndpoints_AreRemoved()
    {
        var client = CreateClient(UserRole.SuperAdmin);
        foreach (var path in new[] { "user", "broadcast", "segment" })
        {
            var response = await client.PostAsync($"/api/admin/notifications/{path}", JsonContent.Create(new { title = "x", body = "y" }));
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{path}: {response.StatusCode}");
        }
    }
}
