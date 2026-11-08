using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MerfitApi.Api.BackgroundJobs;
using MerfitApi.Api.Controllers.Admin;
using MerfitApi.Business.Dtos.Admin.WorkoutLab;
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
/// Antrenman Laboratuvari uc noktalari gercek pipeline'da (JWT + "AdminOnly") korunur: kimliksiz 401, "User" 403,
/// Admin ve SuperAdmin izinli. Gecersiz profil servis cagrilmadan 400 doner; enum'lar ad olarak gonderilebilir.
/// </summary>
public class AdminWorkoutLabAuthorizationTests : IClassFixture<WebApplicationFactory<AdminWorkoutLabController>>
{
    private readonly WebApplicationFactory<AdminWorkoutLabController> _factory;
    private readonly Mock<IAdminWorkoutLabService> _service = new();

    public AdminWorkoutLabAuthorizationTests(WebApplicationFactory<AdminWorkoutLabController> factory)
    {
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

    private static readonly object CreateBody = new
    {
        name = "Ev",
        profile = new { goal = "BuildMuscle", experienceLevel = "Beginner", trainingLocation = "Home", noEquipment = true, trainingDaysPerWeek = 3, workoutDurationMin = 45 },
    };

    public static TheoryData<string, string, string> Endpoints => new()
    {
        { "GET", "/api/admin/workout-lab/options", "" },
        { "GET", "/api/admin/workout-lab/simulations", "" },
        { "GET", "/api/admin/workout-lab/simulations/1", "" },
        { "POST", "/api/admin/workout-lab/simulations", "create" },
        { "POST", "/api/admin/workout-lab/simulations/1/rerun", "" },
        { "PUT", "/api/admin/workout-lab/simulations/1/evaluation", "evaluate" },
        { "DELETE", "/api/admin/workout-lab/simulations/1", "" },
        { "GET", "/api/admin/workout-lab/compare?leftId=1&rightId=2", "" },
    };

    private static HttpRequestMessage Request(string method, string url, string body) => new(new HttpMethod(method), url)
    {
        Content = body switch
        {
            "" => null,
            "create" => JsonContent.Create(CreateBody),
            "evaluate" => JsonContent.Create(new { rating = "Good", comment = "ok" }),
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
    public async Task RegularUser_Gets403_AndServiceIsNeverCalled(string method, string url, string body)
    {
        var response = await CreateClient(UserRole.User).SendAsync(Request(method, url, body));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_service.Invocations);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AdminAndSuperAdmin_AreAllowed(string method, string url, string body)
    {
        foreach (var role in new[] { UserRole.Admin, UserRole.SuperAdmin })
        {
            var response = await CreateClient(role).SendAsync(Request(method, url, body));
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created, $"{role} {method} {url} -> {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Create_BindsEnumNames_AndPassesAdminId()
    {
        AdminCreateWorkoutLabSimulationRequest? captured = null;
        _service.Setup(s => s.CreateAsync(It.IsAny<AdminCreateWorkoutLabSimulationRequest>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<AdminCreateWorkoutLabSimulationRequest, long?, string?, string?, CancellationToken>((r, _, _, _, _) => captured = r)
            .ReturnsAsync(new AdminWorkoutLabSimulationDetailDto { Id = 7, Status = "Pending" });

        var response = await CreateClient(UserRole.Admin).SendAsync(Request("POST", "/api/admin/workout-lab/simulations", "create"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(FitnessGoal.BuildMuscle, captured!.Profile.Goal);
        Assert.Equal(TrainingLocation.Home, captured.Profile.TrainingLocation);
        _service.Verify(s => s.CreateAsync(It.IsAny<AdminCreateWorkoutLabSimulationRequest>(), 5, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("{\"profile\":{\"trainingDaysPerWeek\":1}}")]
    [InlineData("{\"profile\":{\"workoutDurationMin\":300}}")]
    [InlineData("{\"profile\":{\"age\":5}}")]
    public async Task Create_WithOutOfRangeValues_Gets400_WithoutCallingService(string json)
    {
        var response = await CreateClient(UserRole.Admin).PostAsync("/api/admin/workout-lab/simulations",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_service.Invocations);
    }
}
