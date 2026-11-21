using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MerfitApi.Api.BackgroundJobs;
using MerfitApi.Api.Controllers.Admin;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Interfaces.Services;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.Controllers;

/// <summary>
/// Yorum moderasyonu uclari gercek pipeline'da "AdminOnly" ile korunur: kimliksiz 401, "User" 403 (servis hic
/// cagrilmaz), Admin 200. Islemi yapan admin kimligi token'dan alinir.
/// </summary>
public class AdminProductReviewAuthorizationTests : IClassFixture<WebApplicationFactory<AdminProductReviewController>>
{
    private readonly WebApplicationFactory<AdminProductReviewController> _factory;
    private readonly Mock<IAdminProductReviewService> _reviews = new();

    public AdminProductReviewAuthorizationTests(WebApplicationFactory<AdminProductReviewController> factory)
    {
        _reviews.Setup(s => s.GetListAsync(It.IsAny<AdminProductReviewListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<AdminProductReviewListItemDto>.Create(Array.Empty<AdminProductReviewListItemDto>(), 1, 20, 0));
        _reviews.Setup(s => s.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AdminProductReviewDto());
        _reviews.Setup(s => s.UpdateStatusAsync(It.IsAny<long>(), It.IsAny<AdminUpdateProductReviewStatusRequest>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminProductReviewDto());
        _reviews.Setup(s => s.DeleteAsync(It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminProductReviewDto());
        _reviews.Setup(s => s.DismissReportsAsync(It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminProductReviewDto());
        _reviews.Setup(s => s.GetImageAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminProductReviewImageFile(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg"));

        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationJobWorker)));
            services.AddScoped(_ => _reviews.Object);
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

    public static TheoryData<string, string, object?> Endpoints => new()
    {
        { "GET", "/api/admin/store/reviews?search=protein&rating=1&reportedOnly=true", null },
        { "GET", "/api/admin/store/reviews/1", null },
        { "PATCH", "/api/admin/store/reviews/1/status", new { status = "Hidden", note = "spam" } },
        { "POST", "/api/admin/store/reviews/1/delete", new { note = "hakaret" } },
        { "POST", "/api/admin/store/reviews/1/reports/dismiss", new { } },
        { "GET", "/api/admin/store/reviews/1/images/2", null },
    };

    private static HttpRequestMessage Request(string method, string url, object? body) =>
        new(new HttpMethod(method), url) { Content = body is null ? null : JsonContent.Create(body) };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Anonymous_Gets401(string method, string url, object? body)
    {
        var response = await CreateClient(null).SendAsync(Request(method, url, body));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_reviews.Invocations);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task RegularUser_Gets403_AndServiceIsNotCalled(string method, string url, object? body)
    {
        var response = await CreateClient(UserRole.User).SendAsync(Request(method, url, body));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_reviews.Invocations);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Admin_IsAllowed(string method, string url, object? body)
    {
        var response = await CreateClient(UserRole.Admin).SendAsync(Request(method, url, body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Hide_PassesAdminIdFromToken_And409IsReturnedForInvalidTransition()
    {
        await CreateClient(UserRole.Admin).PatchAsync("/api/admin/store/reviews/3/status", JsonContent.Create(new { status = "Hidden", note = "uygunsuz" }));
        _reviews.Verify(s => s.UpdateStatusAsync(3, It.Is<AdminUpdateProductReviewStatusRequest>(r => r.Status == "Hidden" && r.Note == "uygunsuz"),
            5, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        _reviews.Setup(s => s.UpdateStatusAsync(4, It.IsAny<AdminUpdateProductReviewStatusRequest>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Yorum 'Deleted' durumundan 'Published' durumuna gecirilemez."));
        var conflict = await CreateClient(UserRole.Admin).PatchAsync("/api/admin/store/reviews/4/status", JsonContent.Create(new { status = "Published" }));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Image_IsReturnedAsBinaryWithNoSniff()
    {
        var response = await CreateClient(UserRole.Admin).GetAsync("/api/admin/store/reviews/1/images/2");
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }
}
