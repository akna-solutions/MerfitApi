using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MerfitApi.Api.BackgroundJobs;
using MerfitApi.Api.Controllers.Admin;
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
/// MB Shop admin uc noktalari gercek pipeline'da (JWT + "AdminOnly" policy) korunur: kimliksiz 401, "User" 403,
/// Admin 200. Yetki yalnizca istemcide buton gizlenerek degil, sunucuda uygulanir. Dogrulama hatasi servis
/// cagrilmadan 400, servisin cakisma hatasi 409 doner.
/// </summary>
public class AdminStoreAuthorizationTests : IClassFixture<WebApplicationFactory<AdminProductController>>
{
    private readonly WebApplicationFactory<AdminProductController> _factory;
    private readonly Mock<IAdminStoreCategoryService> _categories = new();
    private readonly Mock<IAdminProductService> _products = new();
    private readonly Mock<IAdminStoreCampaignService> _campaigns = new();
    private readonly Mock<IAdminStoreOrderService> _orders = new();

    public AdminStoreAuthorizationTests(WebApplicationFactory<AdminProductController> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationJobWorker)));
            services.AddScoped(_ => _categories.Object);
            services.AddScoped(_ => _products.Object);
            services.AddScoped(_ => _campaigns.Object);
            services.AddScoped(_ => _orders.Object);
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

    private static readonly object CategoryBody = new { name = "Ekipman", isActive = true };
    private static readonly object ProductBody = new
    {
        name = "Dambıl Seti", description = "Ayarlanabilir dambıl seti", sku = "DMB-1", categoryId = 1, brand = "MB", price = 1000m, stockQuantity = 5,
    };
    private static readonly object CampaignBody = new { title = "Yaz", productIds = new[] { 1 } };
    private static readonly object StatusBody = new { isActive = false };

    public static TheoryData<string, string, string> Endpoints => new()
    {
        { "GET", "/api/admin/store/categories", "" },
        { "POST", "/api/admin/store/categories", "category" },
        { "PUT", "/api/admin/store/categories/1", "category" },
        { "PATCH", "/api/admin/store/categories/1/status", "status" },
        { "DELETE", "/api/admin/store/categories/1", "" },
        { "GET", "/api/admin/store/products", "" },
        { "GET", "/api/admin/store/products/1", "" },
        { "POST", "/api/admin/store/products", "product" },
        { "PUT", "/api/admin/store/products/1", "product" },
        { "PATCH", "/api/admin/store/products/1/status", "status" },
        { "PATCH", "/api/admin/store/products/1/stock", "stock" },
        { "DELETE", "/api/admin/store/products/1", "" },
        { "GET", "/api/admin/store/campaigns", "" },
        { "POST", "/api/admin/store/campaigns", "campaign" },
        { "PUT", "/api/admin/store/campaigns/1", "campaign" },
        { "DELETE", "/api/admin/store/campaigns/1", "" },
        { "GET", "/api/admin/store/orders", "" },
        { "GET", "/api/admin/store/orders/1", "" },
        { "PATCH", "/api/admin/store/orders/1/status", "order-status" },
        { "PATCH", "/api/admin/store/orders/1/payment-status", "payment-status" },
    };

    private static HttpRequestMessage Request(string method, string url, string body) => new(new HttpMethod(method), url)
    {
        Content = body switch
        {
            "" => null,
            "category" => JsonContent.Create(CategoryBody),
            "product" => JsonContent.Create(ProductBody),
            "campaign" => JsonContent.Create(CampaignBody),
            "status" => JsonContent.Create(StatusBody),
            "stock" => JsonContent.Create(new { delta = 3 }),
            "order-status" => JsonContent.Create(new { status = "Preparing" }),
            "payment-status" => JsonContent.Create(new { paymentStatus = "Paid" }),
            _ => throw new ArgumentOutOfRangeException(nameof(body)),
        },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Anonymous_Gets401(string method, string url, string body)
    {
        var response = await CreateClient(null).SendAsync(Request(method, url, body));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task RegularUser_Gets403_AndNothingChanges(string method, string url, string body)
    {
        var response = await CreateClient(UserRole.User).SendAsync(Request(method, url, body));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_products.Invocations);
        Assert.Empty(_orders.Invocations);
        Assert.Empty(_categories.Invocations);
        Assert.Empty(_campaigns.Invocations);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Admin_IsAllowed(string method, string url, string body)
    {
        var response = await CreateClient(UserRole.Admin).SendAsync(Request(method, url, body));
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created, $"{method} {url} -> {(int)response.StatusCode}");
    }

    [Fact]
    public async Task CreateProduct_WithInvalidBody_Gets400_WithoutCallingService()
    {
        var response = await CreateClient(UserRole.Admin).PostAsync("/api/admin/store/products",
            JsonContent.Create(new { name = "x", sku = "bad sku!", price = 0, categoryId = 0 }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_products.Invocations);
    }

    [Fact]
    public async Task InvalidOrderTransition_Gets409()
    {
        _orders.Setup(s => s.UpdateStatusAsync(7, It.IsAny<AdminUpdateStoreOrderStatusRequest>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Siparis 'Delivered' durumundan 'Received' durumuna gecirilemez."));

        var response = await CreateClient(UserRole.Admin).PatchAsync("/api/admin/store/orders/7/status", JsonContent.Create(new { status = "Received" }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("gecirilemez", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AdminUserId_ComesFromToken()
    {
        await CreateClient(UserRole.Admin).PatchAsync("/api/admin/store/products/3/stock", JsonContent.Create(new { delta = -2 }));
        _products.Verify(s => s.AdjustStockAsync(3, It.Is<AdminAdjustStockRequest>(r => r.Delta == -2), 5, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
