using MerfitApi.Business.Dtos.Admin.Notifications;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.Notifications;

public class NotificationTemplateAndRuleTests
{
    private readonly NotificationAdminTestData _data = new();

    private static AdminUpsertNotificationTemplateRequest TemplateRequest(NotificationType type, string title, string body, string? dataJson = null) => new()
    {
        Name = "Sablon " + Guid.NewGuid().ToString("N")[..6],
        Type = type,
        TitleTemplate = title,
        BodyTemplate = body,
        DataJson = dataJson,
    };

    [Fact]
    public async Task Template_RejectsPlaceholdersNotSupportedForItsType()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _data.Templates.CreateAsync(
            TemplateRequest(NotificationType.Announcement, "Merhaba {firstName}", "{mealName} zamanı"), 1, null, null));
        Assert.Contains("{mealName}", ex.Message);

        var created = await _data.Templates.CreateAsync(
            TemplateRequest(NotificationType.NutritionReminder, "{firstName}", "{mealName} kaydını eklemeyi unutma", "{\"screen\":\"nutrition\"}"), 1, null, null);
        Assert.Equal("NutritionReminder", created.Type);
    }

    [Theory]
    [InlineData("{\"screen\":\"https://evil.example\"}")]
    [InlineData("{\"screen\":\"nutrition\",\"nested\":{\"a\":1}}")]
    [InlineData("not json")]
    public async Task Template_RejectsInvalidNavigationData(string dataJson)
    {
        await Assert.ThrowsAsync<AppValidationException>(() => _data.Templates.CreateAsync(
            TemplateRequest(NotificationType.Announcement, "Başlık", "İçerik", dataJson), 1, null, null));
    }

    [Fact]
    public async Task Template_CreateWritesAuditLog()
    {
        var created = await _data.Templates.CreateAsync(TemplateRequest(NotificationType.DailyMotivation, "Günaydın {firstName}", "Küçük bir adım da ilerlemedir."), 7, "1.2.3.4", "ua");

        _data.Audit.Verify(a => a.LogAsync(7, "NOTIFICATION_TEMPLATE_CREATED", nameof(NotificationTemplate), created.Id, "1.2.3.4", "ua",
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Preview_RendersSampleValuesAndDataWithoutSending()
    {
        var preview = await _data.Templates.PreviewAsync(new AdminNotificationMessagePreviewRequest
        {
            Type = NotificationType.WorkoutReminder,
            Title = "{firstName}, {workoutName} seni bekliyor",
            Body = "Hazır olduğunda başlayabilirsin.",
            DataJson = "{\"screen\":\"workoutPlan\"}",
        });

        Assert.Equal("Ayşe, Tüm Vücut Kuvvet seni bekliyor", preview.Title);
        Assert.Equal("workoutPlan", preview.Data["screen"]);
        Assert.Empty(_data.Db.Set<NotificationDispatch>());
    }

    private static AdminUpsertNotificationRuleRequest RuleRequest(NotificationRuleKind kind, params long[] templateIds) => new()
    {
        Name = "Kural",
        Kind = kind,
        LocalTime = "12:30",
        MealType = kind == NotificationRuleKind.NutritionReminder ? MealType.Lunch : null,
        TemplateIds = templateIds.ToList(),
    };

    [Fact]
    public async Task Rule_IsCreatedPaused_AndCannotBeActivatedWithoutActiveMatchingTemplate()
    {
        var inactive = _data.Template(NotificationType.NutritionReminder, active: false);
        var rule = await _data.Rules.CreateAsync(RuleRequest(NotificationRuleKind.NutritionReminder, inactive.Id), 1, null, null);

        Assert.Equal("Paused", rule.Status);
        Assert.Equal("12:30", rule.LocalTime);
        await Assert.ThrowsAsync<ConflictException>(() => _data.Rules.ChangeStatusAsync(rule.Id, NotificationRuleStatus.Active, 1, null, null));

        inactive.IsActive = true;
        var active = await _data.Rules.ChangeStatusAsync(rule.Id, NotificationRuleStatus.Active, 1, null, null);
        Assert.Equal("Active", active.Status);
    }

    [Fact]
    public async Task Rule_RejectsTemplatesOfAnotherType_AndNutritionWithoutMeal()
    {
        var motivation = _data.Template(NotificationType.DailyMotivation);
        await Assert.ThrowsAsync<AppValidationException>(() => _data.Rules.CreateAsync(RuleRequest(NotificationRuleKind.WorkoutReminder, motivation.Id), 1, null, null));

        var request = RuleRequest(NotificationRuleKind.NutritionReminder);
        request.MealType = null;
        await Assert.ThrowsAsync<AppValidationException>(() => _data.Rules.CreateAsync(request, 1, null, null));

        var badTime = RuleRequest(NotificationRuleKind.DailyMotivation);
        badTime.LocalTime = "25:00";
        await Assert.ThrowsAsync<AppValidationException>(() => _data.Rules.CreateAsync(badTime, 1, null, null));
    }

    [Fact]
    public async Task DeactivatingLastActiveTemplateOfActiveRule_IsRejected_AndActiveRuleCannotBeDeleted()
    {
        var template = _data.Template(NotificationType.DailyMotivation);
        var rule = await _data.Rules.CreateAsync(RuleRequest(NotificationRuleKind.DailyMotivation, template.Id), 1, null, null);
        await _data.Rules.ChangeStatusAsync(rule.Id, NotificationRuleStatus.Active, 1, null, null);

        var update = TemplateRequest(NotificationType.DailyMotivation, template.TitleTemplate, template.BodyTemplate);
        update.IsActive = false;
        await Assert.ThrowsAsync<ConflictException>(() => _data.Templates.UpdateAsync(template.Id, update, 1, null, null));
        await Assert.ThrowsAsync<ConflictException>(() => _data.Rules.DeleteAsync(rule.Id, 1, null, null));
        await Assert.ThrowsAsync<ConflictException>(() => _data.Templates.DeleteAsync(template.Id, 1, null, null));

        await _data.Rules.ChangeStatusAsync(rule.Id, NotificationRuleStatus.Inactive, 1, null, null);
        await _data.Rules.DeleteAsync(rule.Id, 1, null, null);
        Assert.Empty(_data.Db.Set<NotificationRuleTemplate>());
    }
}
