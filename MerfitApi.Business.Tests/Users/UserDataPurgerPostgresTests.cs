using MerfitApi.Business.Tests.Notifications;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitApi.Business.Tests.Users;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES tanimliysa calisir. UserDataPurger'in kullaniciyi ve bagli kayitlarini
/// (MerfitCustomerApi'ye ait tablolar dahil) gercek PostgreSQL'de, RESTRICT FK'lere takilmadan sildigini;
/// diger kullanicilara dokunmadigini ve bilinmeyen bir FK varsa hicbir seyi silmedigini dogrular.
/// </summary>
public class UserDataPurgerPostgresTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationJobStoreTests.ConnectionVariable)).Options);

    private static async Task<long> SeedUserWithDataAsync()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        var email = $"purge-{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            PasswordHash = "x", IsActive = true, CreatedAt = now,
        };
        db.Add(user);
        await db.SaveChangesAsync();

        var device = new UserDevice { UserId = user.Id, DeviceToken = Guid.NewGuid().ToString("N"), Platform = Platform.IOS, IsActive = true, CreatedAt = now };
        var notification = new Notification { UserId = user.Id, Type = NotificationType.Announcement, Title = "t", Body = "b", CreatedAt = now };
        var score = new MerfitScore { UserId = user.Id, Score = 10, Period = "2026-10", CalculatedAt = now, CreatedAt = now };
        var meal = new Meal { UserId = user.Id, Date = now, MealType = MealType.Breakfast, CreatedAt = now };
        var ticket = new SupportTicket { UserId = user.Id, Subject = "s", Message = "m", CreatedAt = now };
        var order = new StoreOrder
        {
            UserId = user.Id, OrderNumber = "PG-" + Guid.NewGuid().ToString("N")[..10], IdempotencyKey = Guid.NewGuid().ToString("N"),
            ShippingAddressSnapshot = "{}", CreatedAt = now,
        };
        db.AddRange(device, notification, score, meal, ticket, order,
            new UserProfile { UserId = user.Id, Username = "pg" + Guid.NewGuid().ToString("N")[..12], CreatedAt = now },
            new UserRefreshToken { UserId = user.Id, Token = Guid.NewGuid().ToString("N"), ExpiresAt = now.AddDays(1), CreatedAt = now },
            new WaterLog { UserId = user.Id, Date = now, AmountMl = 250, CreatedAt = now },
            new Cart { UserId = user.Id, CreatedAt = now },
            new UserAddress { UserId = user.Id, Title = "Ev", RecipientName = "A", PhoneNumber = "1", City = "C", District = "D", AddressLine = "L", CreatedAt = now },
            new WorkoutPlan { UserId = user.Id, Name = "Plan", StartDate = now, CreatedAt = now },
            new AuditLog { UserId = user.Id, Action = "PG_TEST", Entity = nameof(ApplicationUser), CreatedAt = now });
        await db.SaveChangesAsync();

        db.AddRange(
            new NotificationDispatch
            {
                UserId = user.Id, Type = NotificationType.Announcement, DedupeKey = Guid.NewGuid().ToString("N"), Title = "t", Body = "b",
                ScheduledForUtc = now, NextAttemptAtUtc = now, NotificationId = notification.Id, CreatedAt = now,
            },
            new MerfitScoreBreakdown { MerfitScoreId = score.Id, Category = "c", Points = 1, CreatedAt = now },
            new SupportTicketMessage { TicketId = ticket.Id, SenderUserId = user.Id, Message = "m", CreatedAt = now });
        await db.SaveChangesAsync();

        // MerfitApi modelinde olmayan, MerfitCustomerApi'ye ait RESTRICT FK'li tablo.
        if (await TableExistsAsync(db, "authentication", "UserAccountToken"))
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO authentication."UserAccountToken" ("UserId", "Purpose", "TokenHash", "NormalizedEmail", "ExpiresAt", "CreatedAt")
                VALUES ({0}, 'EmailVerification', {1}, {2}, now() + interval '1 day', now())
                """, user.Id, Guid.NewGuid().ToString("N"), user.NormalizedEmail);
        }

        return user.Id;
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db, string schema, string table) =>
        await db.Database.SqlQueryRaw<int>(
            """SELECT 1 AS "Value" FROM information_schema.tables WHERE table_schema = {0} AND table_name = {1}""", schema, table).AnyAsync();

    [PostgresFact]
    public async Task Purge_removes_user_and_related_rows_but_keeps_other_users_and_audit_entries()
    {
        var userId = await SeedUserWithDataAsync();
        var otherUserId = await SeedUserWithDataAsync();

        await using (var db = CreateContext())
        {
            await new UserDataPurger(db).PurgeAsync(userId);
        }

        await using var check = CreateContext();
        Assert.False(await check.Set<ApplicationUser>().AnyAsync(u => u.Id == userId));
        Assert.False(await check.Set<UserProfile>().AnyAsync(x => x.UserId == userId));
        Assert.False(await check.Set<StoreOrder>().AnyAsync(x => x.UserId == userId));
        Assert.False(await check.Set<NotificationDispatch>().AnyAsync(x => x.UserId == userId));
        Assert.False(await check.Set<SupportTicketMessage>().AnyAsync(x => x.SenderUserId == userId));
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>(
            """SELECT count(*)::int AS "Value" FROM authentication."UserAccountToken" WHERE "UserId" = {0}""", userId).SingleAsync());

        Assert.True(await check.Set<ApplicationUser>().AnyAsync(u => u.Id == otherUserId));
        Assert.True(await check.Set<StoreOrder>().AnyAsync(x => x.UserId == otherUserId));
        Assert.True(await check.Set<AuditLog>().AnyAsync(a => a.Action == "PG_TEST" && a.UserId == null));
    }

    [PostgresFact]
    public async Task Purge_removes_step_activity_rows_created_by_customer_api()
    {
        var userId = await SeedUserWithDataAsync();
        var otherUserId = await SeedUserWithDataAsync();

        await using (var setup = CreateContext())
        {
            // Tablolar MerfitCustomerApi migration'i (20261120185252_StepActivity) ile olusur; yoksa ayni zorunlu
            // kolonlarla ve RESTRICT FK ile olusturulur.
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE SCHEMA IF NOT EXISTS activity;
                CREATE TABLE IF NOT EXISTS activity."DailyActivity" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "UserId" bigint NOT NULL REFERENCES authentication."ApplicationUser" ("Id") ON DELETE RESTRICT,
                    "Date" date NOT NULL, "TotalSteps" integer NOT NULL, "DistanceMeters" integer NOT NULL,
                    "CaloriesBurned" integer NOT NULL, "DailyGoal" integer NOT NULL, "HourlySteps" integer[] NOT NULL,
                    "UtcOffsetMinutes" integer NOT NULL, "Source" character varying(30) NOT NULL, "IsSuspicious" boolean NOT NULL,
                    "LastSyncedAt" timestamp with time zone NOT NULL, "CreatedAt" timestamp with time zone NOT NULL);
                CREATE TABLE IF NOT EXISTS activity."UserActivitySetting" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "UserId" bigint NOT NULL REFERENCES authentication."ApplicationUser" ("Id") ON DELETE RESTRICT,
                    "DailyStepGoal" integer NOT NULL, "CreatedAt" timestamp with time zone NOT NULL);
                """);
            foreach (var id in new[] { userId, otherUserId })
            {
                await setup.Database.ExecuteSqlRawAsync("""
                    INSERT INTO activity."DailyActivity" ("UserId", "Date", "TotalSteps", "DistanceMeters", "CaloriesBurned", "DailyGoal",
                        "HourlySteps", "UtcOffsetMinutes", "Source", "IsSuspicious", "LastSyncedAt", "CreatedAt")
                    VALUES ({0}, current_date, 100, 76, 3, 10000, array_fill(0, ARRAY[24]), 180, 'pedometer', FALSE, now(), now())
                    """, id);
                await setup.Database.ExecuteSqlRawAsync("""
                    INSERT INTO activity."UserActivitySetting" ("UserId", "DailyStepGoal", "CreatedAt") VALUES ({0}, 10000, now())
                    """, id);
            }
        }

        await using (var db = CreateContext())
        {
            await new UserDataPurger(db).PurgeAsync(userId);
        }

        await using var check = CreateContext();
        Assert.False(await check.Set<ApplicationUser>().AnyAsync(u => u.Id == userId));
        foreach (var table in new[] { "DailyActivity", "UserActivitySetting" })
        {
            Assert.Equal(0, await check.Database.SqlQueryRaw<int>(
                $$"""SELECT count(*)::int AS "Value" FROM activity."{{table}}" WHERE "UserId" = {0}""", userId).SingleAsync());
            Assert.Equal(1, await check.Database.SqlQueryRaw<int>(
                $$"""SELECT count(*)::int AS "Value" FROM activity."{{table}}" WHERE "UserId" = {0}""", otherUserId).SingleAsync());
        }
    }

    [PostgresFact]
    public async Task Unknown_restrict_reference_rolls_back_everything()
    {
        var userId = await SeedUserWithDataAsync();
        var table = "purge_guard_" + Guid.NewGuid().ToString("N")[..8];

        await using (var setup = CreateContext())
        {
            await setup.Database.ExecuteSqlRawAsync($"""
                CREATE TABLE public."{table}" ("UserId" bigint NOT NULL REFERENCES authentication."ApplicationUser" ("Id") ON DELETE RESTRICT);
                INSERT INTO public."{table}" VALUES ({userId});
                """);
        }

        try
        {
            await using (var db = CreateContext())
            {
                await Assert.ThrowsAsync<ConflictException>(() => new UserDataPurger(db).PurgeAsync(userId));
            }

            await using var check = CreateContext();
            Assert.True(await check.Set<ApplicationUser>().AnyAsync(u => u.Id == userId));
            Assert.True(await check.Set<StoreOrder>().AnyAsync(x => x.UserId == userId));
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.Database.ExecuteSqlRawAsync($"""DROP TABLE public."{table}" """);
        }
    }
}
