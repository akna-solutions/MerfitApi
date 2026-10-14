using MerfitApi.Business.Dtos.Admin.Ai;
using MerfitApi.Business.Services.Admin;
using MerfitApi.Business.Tests.Notifications;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitApi.Business.Tests.Ai;

/// <summary>
/// MerfitCustomerApi'nin beslenme asistani kullanim kayitlari (Type = "NutritionAssistant") admin AI
/// ekraninda okunabilmeli: enum degeri olmadan EF metin kolonunu enum'a cevirirken hata verirdi.
/// </summary>
public class AdminAiNutritionAssistantTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationJobStoreTests.ConnectionVariable)).Options);

    [PostgresFact]
    public Task NutritionAssistantRequests_AreListedFilteredAndCounted() => AssertListedFilteredAndCounted(AiRequestType.NutritionAssistant);

    /// <summary>MerfitCustomerApi fotograf analizi kayitlari (Type = "FoodPhotoAnalysis") da okunabilmeli.</summary>
    [PostgresFact]
    public Task FoodPhotoAnalysisRequests_AreListedFilteredAndCounted() => AssertListedFilteredAndCounted(AiRequestType.FoodPhotoAnalysis);

    private static async Task AssertListedFilteredAndCounted(AiRequestType type)
    {
        long userId;
        long requestId;
        var model = "test-" + Guid.NewGuid().ToString("N");
        await using (var db = CreateContext())
        {
            var email = $"ai-{Guid.NewGuid():N}@test.local";
            var user = new ApplicationUser
            {
                Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
            };
            db.Set<ApplicationUser>().Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;

            var request = new AiGenerationRequest
            {
                UserId = userId, Type = type, Status = AiGenerationStatus.Completed, Model = model,
                Prompt = "Beslenme asistanı sohbeti (sohbet #1, gün 2026-10-13)",
                StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow.AddSeconds(2), CreatedAt = DateTime.UtcNow,
            };
            db.Set<AiGenerationRequest>().Add(request);
            await db.SaveChangesAsync();
            requestId = request.Id;
        }

        await using var read = CreateContext();
        var service = new AdminAiService(new UnitOfWork(read));

        var page = await service.GetRequestsAsync(new AdminAiRequestListRequest { UserId = userId, Type = type });
        var item = Assert.Single(page.Items);
        Assert.Equal(type.ToString(), item.Type);
        Assert.Equal(model, item.Model);

        var detail = await service.GetRequestByIdAsync(requestId);
        Assert.Equal(type.ToString(), detail.Type);

        var stats = await service.GetStatisticsAsync();
        Assert.Contains(stats.RequestTypeDistribution, d => d.Key == type.ToString() && d.Count >= 1);
    }
}
