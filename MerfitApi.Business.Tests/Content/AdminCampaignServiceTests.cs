using MerfitApi.Business.Dtos.Admin.Content;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Business.Services.Admin;
using MerfitApi.Business.Tests.TestHelpers;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.Content;

/// <summary>
/// Kampanya yonetimi: yalnizca Type = "Campaign" kayitlarina dokunma, dogrulama, durum hesaplama, siralama ve audit log.
/// </summary>
public class AdminCampaignServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUnitOfWork _db = new();
    private readonly Mock<IAuditLogService> _audit = new();

    public AdminCampaignServiceTests()
    {
        _db.Register<AppContent>();
    }

    private AdminCampaignService CreateService() => new(_db.Object, _audit.Object, new FixedTimeProvider(Now));

    private AppContent Add(long id, string type = "Campaign", bool isActive = true, DateTime? startAt = null, DateTime? endAt = null, int sortOrder = 0)
    {
        var entity = new AppContent
        {
            Id = id, Key = $"k-{id}", Type = type, Title = $"Kampanya {id}", IsActive = isActive, StartAt = startAt, EndAt = endAt,
            SortOrder = sortOrder, CreatedAt = Now.AddDays(-id),
        };
        _db.Set<AppContent>().Add(entity);
        return entity;
    }

    private static AdminUpsertCampaignRequest Request(string title = "Yaz kampanyasi") => new()
    {
        Title = title,
        Summary = "Kisa ozet",
        Body = "Detay",
        ImageUrl = "https://cdn.example.com/a.jpg",
        Category = "Uyelik",
        SortOrder = 3,
        StartAt = Now,
        EndAt = Now.AddDays(10),
    };

    [Fact]
    public async Task Create_StoresAsCampaign_WithGeneratedKey_AndAudits()
    {
        var result = await CreateService().CreateAsync(Request(), 7, "1.1.1.1", "ua");

        var entity = Assert.Single(_db.Set<AppContent>());
        Assert.Equal("Campaign", entity.Type);
        Assert.StartsWith("campaign-", entity.Key);
        Assert.Equal("Kisa ozet", entity.Description);
        Assert.Equal("Detay", entity.Body);
        Assert.Equal(3, entity.SortOrder);
        Assert.Equal("Live", result.Status);
        _audit.Verify(a => a.LogAsync(7, "CAMPAIGN_CREATED", nameof(AppContent), entity.Id, "1.1.1.1", "ua", null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/a.jpg")]
    [InlineData("/relative/path.jpg")]
    public async Task Create_RejectsNonHttpImageUrl(string url)
    {
        var request = Request();
        request.ImageUrl = url;

        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().CreateAsync(request, 1, null, null));
        Assert.Empty(_db.Set<AppContent>());
    }

    [Fact]
    public async Task Create_RejectsEndBeforeStart_AndBlankTitle()
    {
        var request = Request();
        request.EndAt = request.StartAt!.Value.AddHours(-1);
        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().CreateAsync(request, 1, null, null));
        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().CreateAsync(Request("   "), 1, null, null));
    }

    [Fact]
    public async Task OtherContentTypes_AreNotVisibleOrEditable()
    {
        Add(1, type: "Banner");

        var list = await CreateService().GetListAsync(new AdminCampaignListRequest());
        Assert.Empty(list.Items);
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().UpdateAsync(1, Request(), 1, null, null));
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().DeleteAsync(1, 1, null, null));
        Assert.Single(_db.Set<AppContent>());
    }

    [Fact]
    public async Task Update_KeepsKeyAndType()
    {
        var entity = Add(1);

        var result = await CreateService().UpdateAsync(1, Request("Yeni baslik"), 2, null, null);

        Assert.Equal("k-1", entity.Key);
        Assert.Equal("Campaign", entity.Type);
        Assert.Equal("Yeni baslik", result.Title);
        Assert.Equal(2, entity.UpdatedUser);
    }

    [Fact]
    public async Task List_FiltersByStatus_AndOrdersBySortOrder()
    {
        Add(1, sortOrder: 5);                                  // Live
        Add(2, sortOrder: 1);                                  // Live
        Add(3, startAt: Now.AddDays(2));                       // Scheduled
        Add(4, endAt: Now.AddDays(-1));                        // Ended
        Add(5, isActive: false);                               // Passive

        var service = CreateService();
        var live = await service.GetListAsync(new AdminCampaignListRequest { Status = "Live" });
        Assert.Equal(new long[] { 2, 1 }, live.Items.Select(i => i.Id));
        Assert.Equal(3, (await service.GetListAsync(new AdminCampaignListRequest { Status = "Scheduled" })).Items.Single().Id);
        Assert.Equal(4, (await service.GetListAsync(new AdminCampaignListRequest { Status = "Ended" })).Items.Single().Id);
        Assert.Equal(5, (await service.GetListAsync(new AdminCampaignListRequest { Status = "Passive" })).Items.Single().Id);
        await Assert.ThrowsAsync<AppValidationException>(() => service.GetListAsync(new AdminCampaignListRequest { Status = "Other" }));
    }

    [Fact]
    public async Task UpdateStatus_ArchivesCampaign()
    {
        var entity = Add(1);

        await CreateService().UpdateStatusAsync(1, new AdminUpdateContentStatusRequest { IsActive = false }, 1, null, null);

        Assert.False(entity.IsActive);
        Assert.Equal("Passive", AdminCampaignService.ResolveStatus(entity, Now));
    }

    [Fact]
    public async Task Reorder_UpdatesSortOrder_AndRejectsUnknownOrDuplicateIds()
    {
        var a = Add(1);
        var b = Add(2);
        Add(3, type: "Banner");
        var service = CreateService();

        await service.ReorderAsync(new AdminReorderCampaignsRequest { Items = { new() { Id = 1, SortOrder = 2 }, new() { Id = 2, SortOrder = 1 } } }, 1, null, null);
        Assert.Equal(2, a.SortOrder);
        Assert.Equal(1, b.SortOrder);

        await Assert.ThrowsAsync<NotFoundException>(() => service.ReorderAsync(
            new AdminReorderCampaignsRequest { Items = { new() { Id = 3, SortOrder = 0 } } }, 1, null, null));
        await Assert.ThrowsAsync<AppValidationException>(() => service.ReorderAsync(
            new AdminReorderCampaignsRequest { Items = { new() { Id = 1, SortOrder = 0 }, new() { Id = 1, SortOrder = 1 } } }, 1, null, null));
    }

    [Fact]
    public async Task Delete_RemovesCampaign_AndAudits()
    {
        Add(1);

        await CreateService().DeleteAsync(1, 9, null, null);

        Assert.Empty(_db.Set<AppContent>());
        _audit.Verify(a => a.LogAsync(9, "CAMPAIGN_DELETED", nameof(AppContent), 1, null, null, It.IsAny<object?>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
