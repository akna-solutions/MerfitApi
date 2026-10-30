using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MerfitApi.Api.BackgroundJobs;
using MerfitApi.Api.Controllers.Admin;
using MerfitApi.Api.Json;
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
/// Admin paneli enum'lari ad olarak ("Active") gonderir. Gercek pipeline'da bu isteklerin baglandigini,
/// sayi gonderen istemcilerin bozulmadigini ve yanitlarda enum'larin sayi olarak kaldigini dogrular.
/// </summary>
public class EnumRequestBindingTests : IClassFixture<WebApplicationFactory<AdminNotificationRuleController>>
{
    private readonly WebApplicationFactory<AdminNotificationRuleController> _factory;
    private readonly Mock<IAdminNotificationRuleService> _rules = new();

    public EnumRequestBindingTests(WebApplicationFactory<AdminNotificationRuleController> factory)
    {
        _rules.Setup(s => s.ChangeStatusAsync(It.IsAny<long>(), It.IsAny<NotificationRuleStatus>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminNotificationRuleDto { Id = 1, Status = "Active" });

        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationJobWorker)));
            services.AddScoped(_ => _rules.Object);
        }));
    }

    private async Task<HttpResponseMessage> PatchStatusAsync(string json)
    {
        var client = _factory.CreateClient();
        using (var scope = _factory.Services.CreateScope())
        {
            var (token, _) = scope.ServiceProvider.GetRequiredService<ITokenService>()
                .GenerateAccessToken(new ApplicationUser { Id = 5, Email = "x@test.local", Role = UserRole.Admin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.PatchAsync("/api/admin/notification-rules/1/status", new StringContent(json, Encoding.UTF8, "application/json"));
    }

    [Theory]
    [InlineData("""{"status":"Active"}""", NotificationRuleStatus.Active)]
    [InlineData("""{"status":"inactive"}""", NotificationRuleStatus.Inactive)]
    [InlineData("""{"status":1}""", NotificationRuleStatus.Paused)]
    public async Task Rule_status_accepts_enum_names_and_numbers(string json, NotificationRuleStatus expected)
    {
        var response = await PatchStatusAsync(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _rules.Verify(s => s.ChangeStatusAsync(1, expected, It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("""{"status":"Bogus"}""")]
    [InlineData("""{"status":"7"}""")]
    public async Task Unknown_enum_names_are_still_rejected(string json)
    {
        var response = await PatchStatusAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Enums_are_still_written_as_numbers()
    {
        var options = new JsonSerializerOptions { Converters = { new LenientEnumConverterFactory() } };

        Assert.Equal("""{"S":2,"N":null}""", JsonSerializer.Serialize(new { S = NotificationRuleStatus.Inactive, N = (NotificationRuleStatus?)null }, options));
        Assert.Equal(NotificationRuleStatus.Paused, JsonSerializer.Deserialize<NotificationRuleStatus?>("\"paused\"", options));
    }
}
