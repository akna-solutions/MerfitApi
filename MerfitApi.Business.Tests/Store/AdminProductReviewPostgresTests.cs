using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Services.Admin;
using MerfitApi.Business.Services.Admin.Store;
using MerfitApi.Business.Tests.Notifications;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.Store;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES tanimliysa calisir (ProductReviews migration'i uygulanmis veritabani). Yorum
/// moderasyonu (gizle / yayinla / sil / raporlari kapat), arama-filtre, islem gecmisi, siparis degisince onayli alisveris
/// etiketinin yenilenmesi ve kullanici silindiginde yorum verilerinin temizlenmesi gercek PostgreSQL'de dogrulanir.
/// </summary>
public class AdminProductReviewPostgresTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationJobStoreTests.ConnectionVariable)).Options);

    private static async Task<T> Run<T>(Func<UnitOfWork, AdminProductReviewService, Task<T>> action)
    {
        await using var db = CreateContext();
        var uow = new UnitOfWork(db);
        return await action(uow, new AdminProductReviewService(uow, new AuditLogService(uow, NullLogger<AuditLogService>.Instance)));
    }

    private static Task<T> Run<T>(Func<AdminProductReviewService, Task<T>> action) => Run((_, s) => action(s));

    private static async Task<long> UserAsync(string? username = null)
    {
        await using var db = CreateContext();
        var email = $"review-admin-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        if (username is not null)
        {
            db.Add(new UserProfile { UserId = user.Id, Username = username, FirstName = "Ayşe", LastName = "Kaya", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        return user.Id;
    }

    private static async Task<(long ProductId, string Name)> ProductAsync()
    {
        await using var db = CreateContext();
        var tag = Guid.NewGuid().ToString("N");
        var category = new StoreCategory { Name = "Kat " + tag[..6], Slug = "kat-" + tag, IsActive = true, CreatedAt = DateTime.UtcNow };
        db.StoreCategories.Add(category);
        await db.SaveChangesAsync();
        var product = new Product
        {
            Name = "Kreatin " + tag[..8], Slug = "kreatin-" + tag, Sku = "KR-" + tag[..16], Description = "d", Brand = "MB", CategoryId = category.Id,
            Price = 500, StockQuantity = 10, IsActive = true, CreatedAt = DateTime.UtcNow,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return (product.Id, product.Name);
    }

    private static async Task<long> ReviewAsync(long userId, long productId, int rating = 5, string comment = "Gayet memnun kaldım, tavsiye ederim.",
        ProductReviewStatus status = ProductReviewStatus.Published, bool verified = false)
    {
        await using var db = CreateContext();
        var review = new ProductReview
        {
            ProductId = productId, UserId = userId, Rating = rating, Comment = comment, Status = status, IsVerifiedPurchase = verified, CreatedAt = DateTime.UtcNow,
        };
        db.ProductReviews.Add(review);
        await db.SaveChangesAsync();
        return review.Id;
    }

    private static async Task ReportAsync(long reviewId, long reporter, ProductReviewReportReason reason = ProductReviewReportReason.Spam)
    {
        await using var db = CreateContext();
        db.ProductReviewReports.Add(new ProductReviewReport { ReviewId = reviewId, ReporterUserId = reporter, Reason = reason, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private static async Task<ProductReview> LoadAsync(long id)
    {
        await using var db = CreateContext();
        return await db.ProductReviews.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    [PostgresFact]
    public async Task List_SearchesAndFilters_ByProductRatingStatusAndReports()
    {
        var (product, productName) = await ProductAsync();
        var (other, _) = await ProductAsync();
        var username = "rv" + Guid.NewGuid().ToString("N")[..10];
        var alice = await UserAsync(username);
        var bob = await UserAsync();
        var reporter = await UserAsync();
        var marker = Guid.NewGuid().ToString("N")[..10];

        var r1 = await ReviewAsync(alice, product, 5, $"Kokusu harika {marker} kesinlikle");
        var r2 = await ReviewAsync(bob, product, 1, "Paket hasarlı geldi, memnun değilim.");
        var r3 = await ReviewAsync(bob, other, 3, "Fena değil ama pahalı buldum.", ProductReviewStatus.Hidden);
        var deleted = await ReviewAsync(alice, other, 2, "Silinmiş bir yorum metni burada.", ProductReviewStatus.Deleted);
        await ReportAsync(r2, reporter);

        async Task<long[]> Ids(AdminProductReviewListRequest q) => (await Run(s => s.GetListAsync(q))).Items.Select(i => i.Id).ToArray();

        Assert.Equal(new[] { r2, r1 }, await Ids(new AdminProductReviewListRequest { ProductId = product }));
        Assert.Equal(new[] { r1 }, await Ids(new AdminProductReviewListRequest { Search = marker.ToUpperInvariant() }));
        Assert.Equal(new[] { r1 }, await Ids(new AdminProductReviewListRequest { Search = username, ProductId = product }));
        Assert.Equal(new[] { r2, r1 }, await Ids(new AdminProductReviewListRequest { Search = productName }));
        Assert.Equal(new[] { r2 }, await Ids(new AdminProductReviewListRequest { ProductId = product, Rating = 1 }));
        Assert.Contains(r2, await Ids(new AdminProductReviewListRequest { ReportedOnly = true, PageSize = 100 }));
        Assert.DoesNotContain(r1, await Ids(new AdminProductReviewListRequest { ReportedOnly = true, PageSize = 100 }));
        Assert.Equal(new[] { r3 }, await Ids(new AdminProductReviewListRequest { ProductId = other }));
        Assert.Equal(new[] { r3 }, await Ids(new AdminProductReviewListRequest { ProductId = other, Status = "Hidden" }));
        Assert.Equal(new[] { deleted }, await Ids(new AdminProductReviewListRequest { ProductId = other, Status = "Deleted" }));
        await Assert.ThrowsAsync<AppValidationException>(() => Ids(new AdminProductReviewListRequest { Rating = 0 }));
        await Assert.ThrowsAsync<AppValidationException>(() => Ids(new AdminProductReviewListRequest { Status = "Spam" }));

        var item = (await Run(s => s.GetListAsync(new AdminProductReviewListRequest { ProductId = product, Rating = 1 }))).Items.Single();
        Assert.Equal((1, 1), (item.OpenReportCount, item.ReportCount));
        Assert.Equal(productName, item.ProductName);
        Assert.NotNull(item.UserEmail);
    }

    [PostgresFact]
    public async Task Hide_ThenRepublish_ThenDelete_ClosesReports_AndRecordsHistory()
    {
        var (product, _) = await ProductAsync();
        var author = await UserAsync();
        var reporter = await UserAsync();
        var review = await ReviewAsync(author, product);
        await ReportAsync(review, reporter, ProductReviewReportReason.Offensive);

        var hidden = await Run(s => s.UpdateStatusAsync(review, new AdminUpdateProductReviewStatusRequest { Status = "Hidden", Note = "Hakaret içeriyor" }, 77, "1.1.1.1", "ua"));
        Assert.Equal("Hidden", hidden.Status);
        Assert.Equal("Resolved", hidden.Reports.Single().Status);
        Assert.Equal(new[] { "Published", "Deleted" }, hidden.AllowedStatusTransitions);
        Assert.Equal("Hakaret içeriyor", (await LoadAsync(review)).ModerationNote);

        await Assert.ThrowsAsync<ConflictException>(() => Run(s => s.UpdateStatusAsync(review, new AdminUpdateProductReviewStatusRequest { Status = "Hidden" }, 77, null, null)));

        await ReportAsync(review, await UserAsync());
        var republished = await Run(s => s.UpdateStatusAsync(review, new AdminUpdateProductReviewStatusRequest { Status = "Published" }, 77, null, null));
        Assert.Equal("Published", republished.Status);
        Assert.Contains(republished.Reports, r => r.Status == "Dismissed");

        var deleted = await Run(s => s.DeleteAsync(review, "Kalıcı kaldırıldı", 78, null, null));
        Assert.Equal("Deleted", deleted.Status);
        Assert.NotNull(deleted.DeletedAt);
        Assert.Empty(deleted.AllowedStatusTransitions);
        await Assert.ThrowsAsync<ConflictException>(() => Run(s => s.UpdateStatusAsync(review, new AdminUpdateProductReviewStatusRequest { Status = "Published" }, 77, null, null)));
        await Assert.ThrowsAsync<ConflictException>(() => Run(s => s.DeleteAsync(review, null, 77, null, null)));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(s => s.UpdateStatusAsync(review, new AdminUpdateProductReviewStatusRequest { Status = "Deleted" }, 77, null, null)));

        var history = deleted.History;
        Assert.Equal("CREATED", history[0].Action);
        var adminActions = history.Where(h => h.AdminUserId.HasValue).ToList();
        Assert.Equal(new[] { AdminProductReviewService.HiddenAction, AdminProductReviewService.PublishedAction, AdminProductReviewService.DeletedAction },
            adminActions.Select(h => h.Action));
        Assert.Equal(("Published", "Hidden", "Hakaret içeriyor"), (adminActions[0].FromStatus, adminActions[0].ToStatus, adminActions[0].Note));
        Assert.Equal(78, adminActions[2].AdminUserId);
    }

    [PostgresFact]
    public async Task ConcurrentModeration_OnlyOneAdminWins()
    {
        var (product, _) = await ProductAsync();
        var review = await ReviewAsync(await UserAsync(), product);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
        {
            try { await Run(s => s.UpdateStatusAsync(review, new AdminUpdateProductReviewStatusRequest { Status = "Hidden" }, i, null, null)); return true; }
            catch (ConflictException) { return false; }
        })));

        Assert.Equal(1, results.Count(ok => ok));
    }

    [PostgresFact]
    public async Task DismissReports_KeepsReviewPublished()
    {
        var (product, _) = await ProductAsync();
        var review = await ReviewAsync(await UserAsync(), product);
        await Assert.ThrowsAsync<ConflictException>(() => Run(s => s.DismissReportsAsync(review, null, 1, null, null)));
        await ReportAsync(review, await UserAsync());
        await ReportAsync(review, await UserAsync(), ProductReviewReportReason.Misleading);

        var dto = await Run(s => s.DismissReportsAsync(review, "Rapor haksız", 1, null, null));

        Assert.Equal("Published", dto.Status);
        Assert.All(dto.Reports, r => Assert.Equal("Dismissed", r.Status));
        Assert.Equal(0, dto.OpenReportCount);
        Assert.Contains(dto.History, h => h.Action == AdminProductReviewService.ReportsDismissedAction && h.Note == "Rapor haksız");
    }

    [PostgresFact]
    public async Task Image_IsReturnedForAdmin_EvenWhenReviewHidden()
    {
        var (product, _) = await ProductAsync();
        var review = await ReviewAsync(await UserAsync(), product, status: ProductReviewStatus.Hidden);
        long imageId;
        await using (var db = CreateContext())
        {
            var image = new ProductReviewImage
            {
                ReviewId = review, StorageKey = Guid.NewGuid().ToString("N"), ContentType = "image/png", ByteSize = 3, Width = 100, Height = 100,
                Content = new byte[] { 1, 2, 3 }, CreatedAt = DateTime.UtcNow,
            };
            db.ProductReviewImages.Add(image);
            await db.SaveChangesAsync();
            imageId = image.Id;
        }

        var file = await Run(s => s.GetImageAsync(review, imageId));
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, file.Content);
        Assert.Single((await Run(s => s.GetByIdAsync(review))).Images);
        await Assert.ThrowsAsync<NotFoundException>(() => Run(s => s.GetImageAsync(review + 100000, imageId)));
    }

    // ---------------- Onayli alisveris senkronu ----------------

    private static async Task<long> OrderAsync(long userId, long productId, StorePaymentMethod method, StorePaymentStatus payment, StoreOrderStatus status)
    {
        await using var db = CreateContext();
        var order = new StoreOrder
        {
            OrderNumber = "RVA-" + Guid.NewGuid().ToString("N")[..12], UserId = userId, Status = status, PaymentStatus = payment, PaymentMethod = method,
            Subtotal = 500, TotalAmount = 500, ShippingAddressSnapshot = "{}", IdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAt = DateTime.UtcNow,
            Items =
            {
                new StoreOrderItem
                {
                    ProductId = productId, ProductNameSnapshot = "Urun", SkuSnapshot = "SKU", BrandSnapshot = "MB", UnitListPrice = 500, UnitPrice = 500,
                    Quantity = 1, LineTotal = 500, CreatedAt = DateTime.UtcNow,
                },
            },
        };
        db.StoreOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static Task SetOrderStatus(long orderId, string status) =>
        Run(async (uow, _) => await new AdminStoreOrderService(uow, new Mock<Interfaces.Services.Admin.IAuditLogService>().Object)
            .UpdateStatusAsync(orderId, new AdminUpdateStoreOrderStatusRequest { Status = status }, 9, null, null));

    private static Task SetPayment(long orderId, string status) =>
        Run(async (uow, _) => await new AdminStoreOrderService(uow, new Mock<Interfaces.Services.Admin.IAuditLogService>().Object)
            .UpdatePaymentStatusAsync(orderId, new AdminUpdateStorePaymentStatusRequest { PaymentStatus = status }, 9, null, null));

    [PostgresFact]
    public async Task VerifiedBadge_FollowsOrderDeliveryAndRefund()
    {
        var (product, _) = await ProductAsync();
        var (otherProduct, _) = await ProductAsync();
        var buyer = await UserAsync();
        var bystander = await UserAsync();
        var review = await ReviewAsync(buyer, product);
        var otherReview = await ReviewAsync(buyer, otherProduct);
        var bystanderReview = await ReviewAsync(bystander, product);
        var order = await OrderAsync(buyer, product, StorePaymentMethod.BankTransfer, StorePaymentStatus.Paid, StoreOrderStatus.Shipped);

        await SetOrderStatus(order, "Delivered");
        Assert.True((await LoadAsync(review)).IsVerifiedPurchase);
        Assert.False((await LoadAsync(otherReview)).IsVerifiedPurchase); // siparis bu urunu icermiyor
        Assert.False((await LoadAsync(bystanderReview)).IsVerifiedPurchase); // baska kullanicinin yorumu

        await SetPayment(order, "Refunded");
        Assert.False((await LoadAsync(review)).IsVerifiedPurchase);
    }

    [PostgresFact]
    public async Task VerifiedBadge_IsRemovedWhenCodOrderPaymentFailsAfterDelivery_AndNotGrantedOnCancel()
    {
        var (product, _) = await ProductAsync();
        var buyer = await UserAsync();
        var review = await ReviewAsync(buyer, product);
        var cod = await OrderAsync(buyer, product, StorePaymentMethod.CashOnDelivery, StorePaymentStatus.Pending, StoreOrderStatus.Shipped);
        await SetOrderStatus(cod, "Delivered");
        Assert.True((await LoadAsync(review)).IsVerifiedPurchase);
        await SetPayment(cod, "Failed");
        Assert.False((await LoadAsync(review)).IsVerifiedPurchase);

        var buyer2 = await UserAsync();
        var review2 = await ReviewAsync(buyer2, product, verified: false);
        var cancelled = await OrderAsync(buyer2, product, StorePaymentMethod.BankTransfer, StorePaymentStatus.Paid, StoreOrderStatus.Preparing);
        await SetOrderStatus(cancelled, "Cancelled");
        Assert.False((await LoadAsync(review2)).IsVerifiedPurchase);
    }

    // ---------------- Kullanici silme ----------------

    [PostgresFact]
    public async Task PurgingUser_RemovesTheirReviewData_AndRecountsVotesOnOthersReviews()
    {
        var (product, _) = await ProductAsync();
        var leaving = await UserAsync();
        var staying = await UserAsync();
        var third = await UserAsync();
        var leavingReview = await ReviewAsync(leaving, product);
        var stayingReview = await ReviewAsync(staying, product);
        await using (var db = CreateContext())
        {
            db.ProductReviewVotes.AddRange(
                new ProductReviewVote { ReviewId = stayingReview, UserId = leaving, IsHelpful = true, CreatedAt = DateTime.UtcNow },
                new ProductReviewVote { ReviewId = stayingReview, UserId = third, IsHelpful = true, CreatedAt = DateTime.UtcNow },
                new ProductReviewVote { ReviewId = leavingReview, UserId = staying, IsHelpful = false, CreatedAt = DateTime.UtcNow });
            db.ProductReviewReports.AddRange(
                new ProductReviewReport { ReviewId = stayingReview, ReporterUserId = leaving, Reason = ProductReviewReportReason.Spam, CreatedAt = DateTime.UtcNow },
                new ProductReviewReport { ReviewId = leavingReview, ReporterUserId = staying, Reason = ProductReviewReportReason.Spam, CreatedAt = DateTime.UtcNow });
            db.ProductReviewImages.Add(new ProductReviewImage
            {
                ReviewId = leavingReview, StorageKey = Guid.NewGuid().ToString("N"), ContentType = "image/png", ByteSize = 1, Width = 64, Height = 64, Content = new byte[] { 1 }, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            await db.ProductReviews.Where(r => r.Id == stayingReview).ExecuteUpdateAsync(s => s.SetProperty(r => r.HelpfulCount, 2));
        }

        await using (var db = CreateContext())
        {
            await new UserDataPurger(db).PurgeAsync(leaving);
        }

        await using (var db = CreateContext())
        {
            Assert.False(await db.ProductReviews.AnyAsync(r => r.UserId == leaving));
            Assert.False(await db.ProductReviewVotes.AnyAsync(v => v.UserId == leaving || v.ReviewId == leavingReview));
            Assert.False(await db.ProductReviewReports.AnyAsync(r => r.ReporterUserId == leaving || r.ReviewId == leavingReview));
            Assert.False(await db.ProductReviewImages.AnyAsync(i => i.ReviewId == leavingReview));
            var kept = await db.ProductReviews.SingleAsync(r => r.Id == stayingReview);
            Assert.Equal(1, kept.HelpfulCount);
            Assert.True(await db.ApplicationUsers.AnyAsync(u => u.Id == staying));
        }
    }
}
