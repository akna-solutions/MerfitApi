using System.Text.Json;
using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Business.Services.Admin.Store;
using MerfitApi.Business.Tests.Notifications;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.Store;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES tanimliysa calisir (MbShopStore migration'i uygulanmis veritabani). Admin MB Shop
/// servislerini gercek PostgreSQL'de dogrular: SKU benzersizligi, ana gorsel degisimi, stogun eksiye dusmemesi,
/// siparis/odeme durum gecisleri ve iptalde stogun bir kez iade edilmesi.
/// </summary>
public class AdminStorePostgresTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationJobStoreTests.ConnectionVariable)).Options);

    private static async Task<T> Run<T>(Func<UnitOfWork, IAuditLogService, Task<T>> action)
    {
        await using var db = CreateContext();
        return await action(new UnitOfWork(db), new Mock<IAuditLogService>().Object);
    }

    private static Task Run(Func<UnitOfWork, IAuditLogService, Task> action) =>
        Run<bool>(async (uow, audit) => { await action(uow, audit); return true; });

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 13)];

    private static async Task<long> CategoryAsync()
    {
        var dto = await Run((uow, audit) => new AdminStoreCategoryService(uow, audit).CreateAsync(
            new AdminUpsertStoreCategoryRequest { Name = "Kategori " + Guid.NewGuid().ToString("N")[..8] }, 1, null, null));
        return dto.Id;
    }

    private static AdminUpsertProductRequest ProductRequest(long categoryId, string? sku = null, int stock = 10, params string[] images) => new()
    {
        Name = "Ayarlanabilir Dambıl Şişe " + Guid.NewGuid().ToString("N")[..6],
        Description = "Ev antrenmanlari icin dambil seti.",
        Sku = sku ?? Unique("SKU"),
        CategoryId = categoryId,
        Brand = "MB",
        Price = 1000m,
        DiscountPrice = 800m,
        StockQuantity = stock,
        ImageUrls = images.ToList(),
    };

    private static Task<AdminProductDto> CreateProductAsync(AdminUpsertProductRequest request) =>
        Run((uow, audit) => new AdminProductService(uow, audit).CreateAsync(request, 1, null, null));

    private static async Task<int> StockAsync(long productId)
    {
        await using var db = CreateContext();
        return await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync();
    }

    private static async Task<long> UserAsync()
    {
        await using var db = CreateContext();
        var email = $"admin-store-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Mobil API'nin olusturdugu gibi bir siparis yazar: stok siparis aninda dusulmus olur.</summary>
    private static async Task<long> OrderAsync(long productId, int quantity, StorePaymentMethod method, StorePaymentStatus payment = StorePaymentStatus.Pending)
    {
        var userId = await UserAsync();
        await using var db = CreateContext();
        var product = await db.Products.SingleAsync(p => p.Id == productId);
        product.StockQuantity -= quantity;
        var order = new StoreOrder
        {
            OrderNumber = Unique("MBF"),
            UserId = userId,
            Status = StoreOrderStatus.Received,
            PaymentStatus = payment,
            PaymentMethod = method,
            Subtotal = 1000m * quantity,
            DiscountAmount = 200m * quantity,
            ShippingCost = 0,
            TotalAmount = 800m * quantity,
            ShippingAddressSnapshot = "{\"title\":\"Ev\",\"recipientName\":\"Ayşe Yılmaz\",\"phoneNumber\":\"5551112233\",\"city\":\"İstanbul\",\"district\":\"Kadıköy\",\"addressLine\":\"Moda Cad. No:1\",\"sourceAddressId\":1}",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            Items =
            {
                new StoreOrderItem
                {
                    ProductId = productId, ProductNameSnapshot = product.Name, SkuSnapshot = product.Sku, BrandSnapshot = product.Brand,
                    UnitListPrice = 1000m, UnitPrice = 800m, Quantity = quantity, DiscountAmount = 200m * quantity, LineTotal = 800m * quantity, CreatedAt = DateTime.UtcNow,
                },
            },
        };
        db.StoreOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static Task<AdminStoreOrderDto> SetStatus(long orderId, string status) =>
        Run((uow, audit) => new AdminStoreOrderService(uow, audit).UpdateStatusAsync(orderId, new AdminUpdateStoreOrderStatusRequest { Status = status }, 9, null, null));

    private static Task<AdminStoreOrderDto> SetPayment(long orderId, string status, string? reference = null) =>
        Run((uow, audit) => new AdminStoreOrderService(uow, audit).UpdatePaymentStatusAsync(orderId,
            new AdminUpdateStorePaymentStatusRequest { PaymentStatus = status, PaymentReference = reference }, 9, null, null));

    [PostgresFact]
    public async Task Product_CreateGeneratesTurkishSlug_AndDuplicateSkuIsRejectedWith409()
    {
        var category = await CategoryAsync();
        var request = ProductRequest(category, images: "https://cdn.test/a.jpg");
        request.Variants.Add(new AdminProductVariantGroupDto
        {
            Id = "Size", Label = "Beden", Kind = "size",
            Options = { new AdminProductVariantOptionDto { Id = "m", Label = "M" }, new AdminProductVariantOptionDto { Id = "xl", Label = "XL", PriceDelta = 50 } },
        });
        var created = await CreateProductAsync(request);

        Assert.StartsWith("ayarlanabilir-dambil-sise-", created.Slug);
        Assert.Equal(request.Sku.ToUpperInvariant(), created.Sku);
        Assert.Equal("size", created.Variants.Single().Id);
        Assert.Equal("https://cdn.test/a.jpg", created.ImageUrl);

        // Mobil API (MerfitCustomerApi StoreJson) ayni camelCase JSON'u okur.
        await using (var db = CreateContext())
        {
            var json = await db.Products.Where(p => p.Id == created.Id).Select(p => p.VariantsJson).SingleAsync();
            using var doc = JsonDocument.Parse(json!);
            Assert.Equal(50m, doc.RootElement[0].GetProperty("options")[1].GetProperty("priceDelta").GetDecimal());
        }

        var duplicate = ProductRequest(category, sku: request.Sku.ToLowerInvariant());
        var ex = await Assert.ThrowsAsync<ConflictException>(() => CreateProductAsync(duplicate));
        Assert.Contains("SKU", ex.Message);
    }

    [PostgresFact]
    public async Task Product_InvalidPricesAndVariantsAreRejected()
    {
        var category = await CategoryAsync();
        var badDiscount = ProductRequest(category);
        badDiscount.DiscountPrice = 1000m;
        await Assert.ThrowsAsync<AppValidationException>(() => CreateProductAsync(badDiscount));

        var badVariant = ProductRequest(category);
        badVariant.Variants.Add(new AdminProductVariantGroupDto
        {
            Id = "paket", Label = "Paket", Options = { new AdminProductVariantOptionDto { Id = "mini", Label = "Mini", PriceDelta = -800m } },
        });
        await Assert.ThrowsAsync<AppValidationException>(() => CreateProductAsync(badVariant));

        var httpImage = ProductRequest(category, images: "http://cdn.test/a.jpg");
        await Assert.ThrowsAsync<AppValidationException>(() => CreateProductAsync(httpImage));
    }

    [PostgresFact]
    public async Task Product_UpdateSwapsPrimaryImage_AndKeepsStock()
    {
        var category = await CategoryAsync();
        var created = await CreateProductAsync(ProductRequest(category, stock: 7, images: new[] { "https://cdn.test/1.jpg", "https://cdn.test/2.jpg" }));

        var update = ProductRequest(category, sku: created.Sku, stock: 999, images: new[] { "https://cdn.test/2.jpg", "https://cdn.test/3.jpg" });
        update.Slug = created.Slug;
        var updated = await Run((uow, audit) => new AdminProductService(uow, audit).UpdateAsync(created.Id, update, 1, null, null));

        Assert.Equal(new[] { "https://cdn.test/2.jpg", "https://cdn.test/3.jpg" }, updated.Images.Select(i => i.ImageUrl));
        Assert.True(updated.Images[0].IsPrimary);
        Assert.False(updated.Images[1].IsPrimary);
        Assert.Equal(7, updated.StockQuantity);
    }

    [PostgresFact]
    public async Task Stock_CannotGoNegative_EvenUnderConcurrentAdjustments()
    {
        var category = await CategoryAsync();
        var product = await CreateProductAsync(ProductRequest(category, stock: 5));

        await Assert.ThrowsAsync<ConflictException>(() => Run((uow, audit) =>
            new AdminProductService(uow, audit).AdjustStockAsync(product.Id, new AdminAdjustStockRequest { Delta = -6 }, 1, null, null)));

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            try
            {
                await Run((uow, audit) => new AdminProductService(uow, audit).AdjustStockAsync(product.Id, new AdminAdjustStockRequest { Delta = -1 }, 1, null, null));
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
        })));

        Assert.Equal(5, results.Count(r => r));
        Assert.Equal(0, await StockAsync(product.Id));
    }

    [PostgresFact]
    public async Task Delete_IsBlockedForCategoryWithProducts_AndForOrderedProduct()
    {
        var category = await CategoryAsync();
        var product = await CreateProductAsync(ProductRequest(category));
        await OrderAsync(product.Id, 1, StorePaymentMethod.CashOnDelivery);

        await Assert.ThrowsAsync<ConflictException>(() => Run((uow, audit) => new AdminStoreCategoryService(uow, audit).DeleteAsync(category, 1, null, null)));
        await Assert.ThrowsAsync<ConflictException>(() => Run((uow, audit) => new AdminProductService(uow, audit).DeleteAsync(product.Id, 1, null, null)));

        var unused = await CreateProductAsync(ProductRequest(await CategoryAsync()));
        await Run((uow, audit) => new AdminProductService(uow, audit).DeleteAsync(unused.Id, 1, null, null));
        await Assert.ThrowsAsync<NotFoundException>(() => Run((uow, audit) => new AdminProductService(uow, audit).GetByIdAsync(unused.Id)));
    }

    [PostgresFact]
    public async Task OrderTransitions_FollowRules_AndBankTransferMustBePaidBeforeShipping()
    {
        var product = await CreateProductAsync(ProductRequest(await CategoryAsync(), stock: 10));
        var orderId = await OrderAsync(product.Id, 2, StorePaymentMethod.BankTransfer);

        await Assert.ThrowsAsync<ConflictException>(() => SetStatus(orderId, "Shipped"));
        await Assert.ThrowsAsync<ConflictException>(() => SetStatus(orderId, "Delivered"));
        await Assert.ThrowsAsync<AppValidationException>(() => SetStatus(orderId, "Teleported"));

        var preparing = await SetStatus(orderId, "Preparing");
        Assert.DoesNotContain("Shipped", preparing.AllowedStatusTransitions);
        await Assert.ThrowsAsync<ConflictException>(() => SetStatus(orderId, "Shipped"));

        var paid = await SetPayment(orderId, "Paid", "DEKONT-42");
        Assert.Equal("Paid", paid.PaymentStatus);
        Assert.Equal("DEKONT-42", paid.PaymentReference);
        Assert.NotNull(paid.PaidAt);

        await SetStatus(orderId, "Shipped");
        var delivered = await SetStatus(orderId, "Delivered");

        Assert.Equal("Delivered", delivered.Status);
        Assert.Empty(delivered.AllowedStatusTransitions);
        Assert.Equal(4, delivered.History.Count);
        Assert.All(delivered.History, h => Assert.Equal("admin:9", h.ChangedBy));
        Assert.Equal("Ayşe Yılmaz", delivered.ShippingAddress.RecipientName);
        await Assert.ThrowsAsync<ConflictException>(() => SetStatus(orderId, "Cancelled"));
        Assert.Equal(8, await StockAsync(product.Id));

        // Her gecis musteriye tek bir MB Shop bildirimi (StoreOrderUpdate) kuyruga yazar; reddedilen gecisler yazmaz.
        var dispatches = await StoreOrderDispatchesAsync(orderId);
        Assert.Equal(
            new[] { "status:Preparing", "payment:Paid", "status:Shipped", "status:Delivered" },
            dispatches.Select(d => d.DedupeKey.Split(':', 3)[2]));
        Assert.All(dispatches, d =>
        {
            Assert.Equal(NotificationType.StoreOrderUpdate, d.Type);
            Assert.Equal(NotificationDispatchSource.StoreOrder, d.Source);
            Assert.Equal(NotificationDispatchStatus.Pending, d.Status);
            Assert.Contains("\"screen\":\"storeOrder\"", d.DataJson);
            Assert.Contains($"\"orderId\":\"{orderId}\"", d.DataJson);
        });
    }

    private static async Task<List<NotificationDispatch>> StoreOrderDispatchesAsync(long orderId)
    {
        await using var db = CreateContext();
        var prefix = $"store-order:{orderId}:";
        return await db.NotificationDispatches.AsNoTracking().Where(d => d.DedupeKey.StartsWith(prefix)).OrderBy(d => d.Id).ToListAsync();
    }

    [PostgresFact]
    public async Task ConcurrentCancels_RestoreStockExactlyOnce()
    {
        var product = await CreateProductAsync(ProductRequest(await CategoryAsync(), stock: 10));
        var orderId = await OrderAsync(product.Id, 3, StorePaymentMethod.CashOnDelivery);
        Assert.Equal(7, await StockAsync(product.Id));

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            try
            {
                await SetStatus(orderId, "Cancelled");
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
        })));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(10, await StockAsync(product.Id));
        var order = await Run((uow, audit) => new AdminStoreOrderService(uow, audit).GetByIdAsync(orderId));
        Assert.Equal("Cancelled", order.Status);
        Assert.Equal("Cancelled", order.PaymentStatus);
        Assert.NotNull(order.CancelledAt);
        var dispatch = Assert.Single(await StoreOrderDispatchesAsync(orderId));
        Assert.Equal("Siparişin iptal edildi", dispatch.Title);
    }

    [PostgresFact]
    public async Task PaymentFailed_CancelsUnshippedOrder_AndConcurrentResultsCannotBothWin()
    {
        var product = await CreateProductAsync(ProductRequest(await CategoryAsync(), stock: 10));
        var failedOrder = await OrderAsync(product.Id, 2, StorePaymentMethod.BankTransfer);

        var failed = await SetPayment(failedOrder, "Failed");
        Assert.Equal("Cancelled", failed.Status);
        Assert.Equal("Failed", failed.PaymentStatus);
        Assert.Equal(10, await StockAsync(product.Id));
        await Assert.ThrowsAsync<ConflictException>(() => SetPayment(failedOrder, "Paid"));
        var failedDispatch = Assert.Single(await StoreOrderDispatchesAsync(failedOrder));
        Assert.Equal("Ödemen alınamadı", failedDispatch.Title);

        var raced = await OrderAsync(product.Id, 1, StorePaymentMethod.BankTransfer);
        var outcomes = await Task.WhenAll(new[] { "Paid", "Failed", "Paid", "Failed" }.Select(status => Task.Run(async () =>
        {
            try
            {
                await SetPayment(raced, status);
                return status;
            }
            catch (ConflictException)
            {
                return null;
            }
        })));

        var winner = Assert.Single(outcomes.OfType<string>());
        Assert.Single(await StoreOrderDispatchesAsync(raced));
        var order = await Run((uow, audit) => new AdminStoreOrderService(uow, audit).GetByIdAsync(raced));
        Assert.Equal(winner, order.PaymentStatus);
        Assert.Equal(winner == "Failed" ? 10 : 9, await StockAsync(product.Id));
    }

    [PostgresFact]
    public async Task OrderList_FiltersByStatusPaymentAndSearch()
    {
        var product = await CreateProductAsync(ProductRequest(await CategoryAsync(), stock: 10));
        var orderId = await OrderAsync(product.Id, 1, StorePaymentMethod.BankTransfer);
        var detail = await SetStatus(orderId, "Preparing");

        var list = await Run((uow, audit) => new AdminStoreOrderService(uow, audit).GetListAsync(new AdminStoreOrderListRequest
        {
            Status = "preparing", PaymentStatus = "Pending", PaymentMethod = "BankTransfer", Search = detail.OrderNumber.ToLowerInvariant(),
        }));

        var item = Assert.Single(list.Items);
        Assert.Equal(orderId, item.Id);
        Assert.Equal(1, item.ItemCount);
        Assert.Equal("Ayşe Yılmaz", item.RecipientName);
        Assert.NotNull(item.UserEmail);

        await Assert.ThrowsAsync<AppValidationException>(() => Run((uow, audit) =>
            new AdminStoreOrderService(uow, audit).GetListAsync(new AdminStoreOrderListRequest { Status = "1" })));
    }

    [PostgresFact]
    public async Task Campaign_KeepsProductOrder_AndRejectsUnknownProducts()
    {
        var category = await CategoryAsync();
        var a = await CreateProductAsync(ProductRequest(category));
        var b = await CreateProductAsync(ProductRequest(category));

        var campaign = await Run((uow, audit) => new AdminStoreCampaignService(uow, audit).CreateAsync(
            new AdminUpsertStoreCampaignRequest { Title = "Yaz Kampanyası", Tint = "#ff8800", ProductIds = { b.Id, a.Id, b.Id } }, 1, null, null));
        Assert.Equal(new[] { b.Id, a.Id }, campaign.ProductIds);
        Assert.Equal("#FF8800", campaign.Tint);

        var updated = await Run((uow, audit) => new AdminStoreCampaignService(uow, audit).UpdateAsync(campaign.Id,
            new AdminUpsertStoreCampaignRequest { Title = "Yaz Kampanyası", ProductIds = { a.Id } }, 1, null, null));
        Assert.Equal(new[] { a.Id }, updated.ProductIds);

        await Assert.ThrowsAsync<AppValidationException>(() => Run((uow, audit) => new AdminStoreCampaignService(uow, audit).UpdateAsync(campaign.Id,
            new AdminUpsertStoreCampaignRequest { Title = "Yaz", ProductIds = { long.MaxValue } }, 1, null, null)));
    }
}
