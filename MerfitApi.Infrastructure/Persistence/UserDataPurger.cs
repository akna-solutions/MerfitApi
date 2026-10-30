using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MerfitApi.Infrastructure.Persistence;

/// <summary>
/// IUserDataPurger'in PostgreSQL implementasyonu. Silme sirasi yapraktan koke dogrudur
/// (once alt kayitlar, en son authentication."ApplicationUser"); boylece RESTRICT tanimli FK'ler
/// silmeyi engellemez. Listede MerfitCustomerApi'ye ait tablolar da (orn. UserAccountToken,
/// NutritionAssistantConversation) bulunur; ortamda olmayan tablolar atlanir.
/// </summary>
public class UserDataPurger : IUserDataPurger
{
    /// <summary>
    /// (Silinecek tablo, DELETE ifadesi) ciftleri. {0} kullanici Id parametresidir. Alt sorgulardaki
    /// ust tablolar (orn. workout."WorkoutSession") her ortamda bulunan cekirdek tablolardir.
    /// </summary>
    private static readonly (string Table, string Sql)[] Steps =
    {
        // Antrenman
        ("workout.WorkoutSetLog", """
            DELETE FROM workout."WorkoutSetLog" WHERE "WorkoutSessionExerciseId" IN (
                SELECT e."Id" FROM workout."WorkoutSessionExercise" e
                JOIN workout."WorkoutSession" s ON s."Id" = e."WorkoutSessionId" WHERE s."UserId" = {0})
            """),
        ("workout.WorkoutSessionExercise", """
            DELETE FROM workout."WorkoutSessionExercise" WHERE "WorkoutSessionId" IN (
                SELECT "Id" FROM workout."WorkoutSession" WHERE "UserId" = {0})
            """),
        ("workout.PersonalRecord", """DELETE FROM workout."PersonalRecord" WHERE "UserId" = {0}"""),
        ("workout.WorkoutSession", """DELETE FROM workout."WorkoutSession" WHERE "UserId" = {0}"""),
        ("workout.WorkoutPlanExercise", """
            DELETE FROM workout."WorkoutPlanExercise" WHERE "WorkoutPlanDayId" IN (
                SELECT d."Id" FROM workout."WorkoutPlanDay" d
                JOIN workout."WorkoutPlan" p ON p."Id" = d."WorkoutPlanId" WHERE p."UserId" = {0})
            """),
        ("workout.WorkoutPlanDay", """
            DELETE FROM workout."WorkoutPlanDay" WHERE "WorkoutPlanId" IN (
                SELECT "Id" FROM workout."WorkoutPlan" WHERE "UserId" = {0})
            """),
        ("workout.WorkoutPlan", """DELETE FROM workout."WorkoutPlan" WHERE "UserId" = {0}"""),

        // Beslenme
        ("nutrition.MealItem", """
            DELETE FROM nutrition."MealItem" WHERE "MealId" IN (
                SELECT "Id" FROM nutrition."Meal" WHERE "UserId" = {0})
            """),
        ("nutrition.Meal", """DELETE FROM nutrition."Meal" WHERE "UserId" = {0}"""),
        ("nutrition.NutritionPlanMealItem", """
            DELETE FROM nutrition."NutritionPlanMealItem" WHERE "NutritionPlanMealId" IN (
                SELECT m."Id" FROM nutrition."NutritionPlanMeal" m
                JOIN nutrition."NutritionPlanDay" d ON d."Id" = m."NutritionPlanDayId"
                JOIN nutrition."NutritionPlan" p ON p."Id" = d."NutritionPlanId" WHERE p."UserId" = {0})
            """),
        ("nutrition.NutritionPlanMeal", """
            DELETE FROM nutrition."NutritionPlanMeal" WHERE "NutritionPlanDayId" IN (
                SELECT d."Id" FROM nutrition."NutritionPlanDay" d
                JOIN nutrition."NutritionPlan" p ON p."Id" = d."NutritionPlanId" WHERE p."UserId" = {0})
            """),
        ("nutrition.NutritionPlanDay", """
            DELETE FROM nutrition."NutritionPlanDay" WHERE "NutritionPlanId" IN (
                SELECT "Id" FROM nutrition."NutritionPlan" WHERE "UserId" = {0})
            """),
        ("nutrition.NutritionPlan", """DELETE FROM nutrition."NutritionPlan" WHERE "UserId" = {0}"""),
        ("nutrition.NutritionGoal", """DELETE FROM nutrition."NutritionGoal" WHERE "UserId" = {0}"""),
        ("nutrition.WaterLog", """DELETE FROM nutrition."WaterLog" WHERE "UserId" = {0}"""),

        // Yapay zeka
        ("ai.AiGenerationResult", """
            DELETE FROM ai."AiGenerationResult" WHERE "RequestId" IN (
                SELECT "Id" FROM ai."AiGenerationRequest" WHERE "UserId" = {0})
            """),
        ("ai.AiGenerationRequest", """DELETE FROM ai."AiGenerationRequest" WHERE "UserId" = {0}"""),
        ("ai.NutritionAssistantMessage", """
            DELETE FROM ai."NutritionAssistantMessage" WHERE "ConversationId" IN (
                SELECT "Id" FROM ai."NutritionAssistantConversation" WHERE "UserId" = {0})
            """),
        ("ai.NutritionAssistantConversation", """DELETE FROM ai."NutritionAssistantConversation" WHERE "UserId" = {0}"""),

        // Oyunlastirma
        ("gamification.MerfitScoreBreakdown", """
            DELETE FROM gamification."MerfitScoreBreakdown" WHERE "MerfitScoreId" IN (
                SELECT "Id" FROM gamification."MerfitScore" WHERE "UserId" = {0})
            """),
        ("gamification.MerfitScore", """DELETE FROM gamification."MerfitScore" WHERE "UserId" = {0}"""),
        ("gamification.MerfitScoreHistory", """DELETE FROM gamification."MerfitScoreHistory" WHERE "UserId" = {0}"""),
        ("gamification.LeaderboardEntry", """DELETE FROM gamification."LeaderboardEntry" WHERE "UserId" = {0}"""),
        ("gamification.UserAchievement", """DELETE FROM gamification."UserAchievement" WHERE "UserId" = {0}"""),
        ("gamification.UserReward", """DELETE FROM gamification."UserReward" WHERE "UserId" = {0}"""),
        ("gamification.MiniGameSession", """DELETE FROM gamification."MiniGameSession" WHERE "UserId" = {0}"""),

        // Bildirimler
        ("notification.NotificationDeliveryAttempt", """
            DELETE FROM notification."NotificationDeliveryAttempt"
            WHERE "NotificationDispatchId" IN (SELECT "Id" FROM notification."NotificationDispatch" WHERE "UserId" = {0})
               OR "UserDeviceId" IN (SELECT "Id" FROM authentication."UserDevice" WHERE "UserId" = {0})
            """),
        ("notification.NotificationDispatch", """DELETE FROM notification."NotificationDispatch" WHERE "UserId" = {0}"""),
        ("notification.Notification", """DELETE FROM notification."Notification" WHERE "UserId" = {0}"""),
        ("notification.NotificationCampaignUser", """DELETE FROM notification."NotificationCampaignUser" WHERE "UserId" = {0}"""),

        // Abonelik
        ("subscription.SubscriptionTransaction", """
            DELETE FROM subscription."SubscriptionTransaction" WHERE "SubscriptionId" IN (
                SELECT "Id" FROM subscription."Subscription" WHERE "UserId" = {0})
            """),
        ("subscription.Subscription", """DELETE FROM subscription."Subscription" WHERE "UserId" = {0}"""),

        // Destek
        ("support.SupportTicketMessage", """
            DELETE FROM support."SupportTicketMessage"
            WHERE "SenderUserId" = {0}
               OR "TicketId" IN (SELECT "Id" FROM support."SupportTicket" WHERE "UserId" = {0})
            """),
        ("support.SupportTicket", """DELETE FROM support."SupportTicket" WHERE "UserId" = {0}"""),

        // MB Shop
        ("store.OrderItem", """
            DELETE FROM store."OrderItem" WHERE "OrderId" IN (SELECT "Id" FROM store."Order" WHERE "UserId" = {0})
            """),
        ("store.OrderStatusHistory", """
            DELETE FROM store."OrderStatusHistory" WHERE "OrderId" IN (SELECT "Id" FROM store."Order" WHERE "UserId" = {0})
            """),
        ("store.Order", """DELETE FROM store."Order" WHERE "UserId" = {0}"""),
        ("store.CartItem", """
            DELETE FROM store."CartItem" WHERE "CartId" IN (SELECT "Id" FROM store."Cart" WHERE "UserId" = {0})
            """),
        ("store.Cart", """DELETE FROM store."Cart" WHERE "UserId" = {0}"""),
        ("store.Favorite", """DELETE FROM store."Favorite" WHERE "UserId" = {0}"""),
        ("store.Address", """DELETE FROM store."Address" WHERE "UserId" = {0}"""),

        // Profil
        ("profile.BodyMeasurement", """DELETE FROM profile."BodyMeasurement" WHERE "UserId" = {0}"""),
        ("profile.GoalHistory", """DELETE FROM profile."GoalHistory" WHERE "UserId" = {0}"""),
        ("profile.UserGoal", """DELETE FROM profile."UserGoal" WHERE "UserId" = {0}"""),
        ("profile.UserConsent", """DELETE FROM profile."UserConsent" WHERE "UserId" = {0}"""),
        ("profile.UserEquipment", """DELETE FROM profile."UserEquipment" WHERE "UserId" = {0}"""),
        ("profile.UserNotificationSetting", """DELETE FROM profile."UserNotificationSetting" WHERE "UserId" = {0}"""),
        ("profile.UserNotificationTypeSetting", """DELETE FROM profile."UserNotificationTypeSetting" WHERE "UserId" = {0}"""),
        ("profile.UserPreference", """DELETE FROM profile."UserPreference" WHERE "UserId" = {0}"""),
        ("profile.UserPrivacySetting", """DELETE FROM profile."UserPrivacySetting" WHERE "UserId" = {0}"""),
        ("profile.UserStreak", """DELETE FROM profile."UserStreak" WHERE "UserId" = {0}"""),
        ("profile.PersonalizationJob", """DELETE FROM profile."PersonalizationJob" WHERE "UserId" = {0}"""),
        ("profile.UserProfile", """DELETE FROM profile."UserProfile" WHERE "UserId" = {0}"""),

        // Kimlik
        ("authentication.UserDevice", """DELETE FROM authentication."UserDevice" WHERE "UserId" = {0}"""),
        ("authentication.UserRefreshToken", """DELETE FROM authentication."UserRefreshToken" WHERE "UserId" = {0}"""),
        ("authentication.UserAccountToken", """DELETE FROM authentication."UserAccountToken" WHERE "UserId" = {0}"""),
        ("authentication.ExternalLogin", """DELETE FROM authentication."ExternalLogin" WHERE "UserId" = {0}"""),

        // Audit log kayitlari korunur; kullanici referansi null'a cekilir (FK de SET NULL).
        ("audit.AuditLog", """UPDATE audit."AuditLog" SET "UserId" = NULL WHERE "UserId" = {0}"""),

        ("authentication.ApplicationUser", """DELETE FROM authentication."ApplicationUser" WHERE "Id" = {0}"""),
    };

    private readonly AppDbContext _dbContext;

    public UserDataPurger(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task PurgeAsync(long userId, CancellationToken cancellationToken = default)
    {
        var existingTables = (await _dbContext.Database
                .SqlQueryRaw<string>("""SELECT table_schema || '.' || table_name AS "Value" FROM information_schema.tables""")
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var (table, sql) in Steps)
            {
                if (!existingTables.Contains(table))
                {
                    continue;
                }

                await _dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { userId }, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException(
                $"Kullanici silinemedi: '{ex.TableName}' tablosunda bu kullaniciya bagli kayitlar var. Hicbir veri silinmedi.");
        }

        // Silinen satirlar baska bir islemde izleniyorsa eski hallerini kullanmamak icin.
        _dbContext.ChangeTracker.Clear();
    }
}
