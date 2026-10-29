using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MerfitApi.Api.BackgroundJobs;
using MerfitApi.Api.Controllers.Admin;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Content;
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
/// Kampanya admin uc noktalari gercek pipeline'da (JWT + "AdminOnly" policy) korunur: kimliksiz 401, "User" 403,
/// Admin 200. Istek dogrulamasi (zorunlu baslik) servis cagrilmadan 400 dondurur.
/// </summary>
public class AdminCampaignAuthorizationTests : IClassFixture<WebApplicationFactory<AdminCampaignController>>
{
    private readonly WebApplicationFactory<AdminCampaignController> _factory;
    private readonly Mock<IAdminCampaignService> _service = new();

    public AdminCampaignAuthorizationTests(WebApplicationFactory<AdminCampaignController> factory)
    {
        _service.Setup(s => s.GetListAsync(It.IsAny<AdminCampaignListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<AdminCampaignDto>.Create(Array.Empty<AdminCampaignDto>(), 1, 20, 0));
        _service.Setup(s => s.UpdateAsync(It.IsAny<long>(), It.IsAny<AdminUpsertCampaignRequest>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminCampaignDto { Id = 1, Title = "x" });

        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationJobWorker)));
            services.AddScoped(_ => _service.Object);
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
        { "GET", "/api/admin/campaigns" },
        { "PUT", "/api/admin/campaigns/1" },
        { "PATCH", "/api/admin/campaigns/1/status" },
        { "PUT", "/api/admin/campaigns/order" },
        { "DELETE", "/api/admin/campaigns/1" },
    };

    private static HttpRequestMessage Request(string method, string url) => new(new HttpMethod(method), url)
    {
        Content = method is "GET" or "DELETE" ? null : JsonContent.Create(new
        {
            title = "Kampanya",
            isActive = true,
            items = new[] { new { id = 1, sortOrder = 0 } },
        }),
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
        _service.Verify(s => s.DeleteAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Admin_IsAllowed(string method, string url)
    {
        var response = await CreateClient(UserRole.Admin).SendAsync(Request(method, url));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutTitle_Gets400()
    {
        var response = await CreateClient(UserRole.Admin).PostAsync("/api/admin/campaigns", JsonContent.Create(new { summary = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _service.Verify(s => s.CreateAsync(It.IsAny<AdminUpsertCampaignRequest>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
