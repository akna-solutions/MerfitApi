using System.Text.Json;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Store;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin.Store;

/// <summary>
/// MB Shop siparis yonetimi. Siparis durumu ile odeme durumu ayri izlenir ve yalnizca tanimli gecislerle degisir:
///   Siparis: Received -> Preparing | Cancelled, Preparing -> Shipped | Cancelled, Shipped -> Delivered.
///   Odeme:   Pending -> Paid | Failed, Paid -> RefundPending | Refunded, RefundPending -> Refunded.
/// Iptalde (ve kargoya verilmemis sipariste odeme basarisiz olunca) stok iade edilir. Status iyimser eszamanlilik
/// belirteci oldugu icin ayni siparis iki kez iptal edilip stok iki kez iade edilemez; odeme durumu kosullu UPDATE
/// ile degistirilir (iki admin ayni anda farkli sonuc yazamaz).
/// </summary>
public class AdminStoreOrderService : IAdminStoreOrderService
{
    private static readonly Dictionary<StoreOrderStatus, StoreOrderStatus[]> StatusTransitions = new()
    {
        [StoreOrderStatus.Received] = new[] { StoreOrderStatus.Preparing, StoreOrderStatus.Cancelled },
        [StoreOrderStatus.Preparing] = new[] { StoreOrderStatus.Shipped, StoreOrderStatus.Cancelled },
        [StoreOrderStatus.Shipped] = new[] { StoreOrderStatus.Delivered },
        [StoreOrderStatus.Delivered] = Array.Empty<StoreOrderStatus>(),
        [StoreOrderStatus.Cancelled] = Array.Empty<StoreOrderStatus>(),
    };

    private static readonly Dictionary<StorePaymentStatus, StorePaymentStatus[]> PaymentTransitions = new()
    {
        [StorePaymentStatus.Pending] = new[] { StorePaymentStatus.Paid, StorePaymentStatus.Failed },
        [StorePaymentStatus.Paid] = new[] { StorePaymentStatus.RefundPending, StorePaymentStatus.Refunded },
        [StorePaymentStatus.RefundPending] = new[] { StorePaymentStatus.Refunded },
        [StorePaymentStatus.Failed] = Array.Empty<StorePaymentStatus>(),
        [StorePaymentStatus.Refunded] = Array.Empty<StorePaymentStatus>(),
        [StorePaymentStatus.Cancelled] = Array.Empty<StorePaymentStatus>(),
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;
    private readonly TimeProvider _timeProvider;

    public AdminStoreOrderService(IUnitOfWork unitOfWork, IAuditLogService auditLogService, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<AdminStoreOrderListItemDto>> GetListAsync(AdminStoreOrderListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<StoreOrder>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = AdminStoreInput.ParseEnum<StoreOrderStatus>(request.Status, nameof(request.Status), "Gecersiz siparis durumu filtresi.");
            query = query.Where(o => o.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentStatus))
        {
            var payment = AdminStoreInput.ParseEnum<StorePaymentStatus>(request.PaymentStatus, nameof(request.PaymentStatus), "Gecersiz odeme durumu filtresi.");
            query = query.Where(o => o.PaymentStatus == payment);
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            var method = AdminStoreInput.ParseEnum<StorePaymentMethod>(request.PaymentMethod, nameof(request.PaymentMethod), "Gecersiz odeme yontemi filtresi.");
            query = query.Where(o => o.PaymentMethod == method);
        }

        if (request.UserId.HasValue) query = query.Where(o => o.UserId == request.UserId.Value);

        var from = AdminStoreInput.ToUtc(request.From);
        var to = AdminStoreInput.ToUtc(request.To);
        if (from.HasValue && to.HasValue && to.Value <= from.Value)
        {
            throw new AppValidationException(nameof(request.To), "Bitis tarihi baslangic tarihinden sonra olmalidir.");
        }

        if (from.HasValue) query = query.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(o => o.CreatedAt < to.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            var matchingUsers = users.Where(u => u.Email != null && u.Email.ToLower().Contains(term)).Select(u => u.Id);
            query = query.Where(o => o.OrderNumber.ToLower().Contains(term) || matchingUsers.Contains(o.UserId));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var desc = !string.Equals(request.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<StoreOrder> ordered = request.SortBy?.Trim().ToLowerInvariant() switch
        {
            "total" or "totalamount" => desc ? query.OrderByDescending(o => o.TotalAmount) : query.OrderBy(o => o.TotalAmount),
            null or "" or "createdat" => desc ? query.OrderByDescending(o => o.CreatedAt) : query.OrderBy(o => o.CreatedAt),
            _ => throw new AppValidationException(nameof(request.SortBy), "Gecersiz siralama alani."),
        };

        var rows = await ordered.ThenByDescending(o => o.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var emails = await users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);
        var counts = await _unitOfWork.Repository<StoreOrderItem>().GetQueryable()
            .Where(i => ids.Contains(i.OrderId))
            .GroupBy(i => i.OrderId)
            .Select(g => new { g.Key, Count = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        var items = rows.Select(o => Fill(new AdminStoreOrderListItemDto(), o, emails.GetValueOrDefault(o.UserId), counts.GetValueOrDefault(o.Id))).ToList();
        return PagedResult<AdminStoreOrderListItemDto>.Create(items, request.Page, request.PageSize, total);
    }

    public async Task<AdminStoreOrderDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var order = await _unitOfWork.Repository<StoreOrder>().GetQueryable()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException("Siparis", id);
        var email = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == order.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
        var history = await _unitOfWork.Repository<StoreOrderStatusHistory>().GetQueryable()
            .Where(h => h.OrderId == id)
            .OrderBy(h => h.CreatedAt).ThenBy(h => h.Id)
            .ToListAsync(cancellationToken);

        var dto = Fill(new AdminStoreOrderDto(), order, email, order.Items.Sum(i => i.Quantity));
        dto.Subtotal = order.Subtotal;
        dto.DiscountAmount = order.DiscountAmount;
        dto.ShippingCost = order.ShippingCost;
        dto.PaymentProvider = order.PaymentProvider;
        dto.PaymentReference = order.PaymentReference;
        dto.PaidAt = order.PaidAt;
        dto.CancelledAt = order.CancelledAt;
        dto.CancelReason = order.CancelReason;
        dto.ShippingAddress = ReadAddress(order.ShippingAddressSnapshot);
        dto.Items = order.Items.OrderBy(i => i.Id).Select(i => new AdminStoreOrderItemDto
        {
            Id = i.Id,
            ProductId = i.ProductId,
            ProductName = i.ProductNameSnapshot,
            Sku = i.SkuSnapshot,
            Brand = i.BrandSnapshot,
            ImageUrl = i.ImageUrlSnapshot,
            VariantLabel = i.VariantLabel,
            UnitListPrice = i.UnitListPrice,
            UnitPrice = i.UnitPrice,
            Quantity = i.Quantity,
            DiscountAmount = i.DiscountAmount,
            LineTotal = i.LineTotal,
        }).ToList();
        dto.History = history.Select(h => new AdminStoreOrderHistoryDto
        {
            Status = h.Status.ToString(),
            PaymentStatus = h.PaymentStatus.ToString(),
            ChangedBy = h.ChangedBy,
            Note = h.Note,
            CreatedAt = h.CreatedAt,
        }).ToList();
        dto.AllowedStatusTransitions = AllowedStatuses(order).Select(s => s.ToString()).ToList();
        dto.AllowedPaymentTransitions = PaymentTransitions[order.PaymentStatus].Select(s => s.ToString()).ToList();
        return dto;
    }

    public async Task<AdminStoreOrderDto> UpdateStatusAsync(long id, AdminUpdateStoreOrderStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var target = AdminStoreInput.ParseEnum<StoreOrderStatus>(request.Status, nameof(request.Status), "Gecersiz siparis durumu.");
        var note = AdminStoreInput.NullIfBlank(request.Note);
        var order = await LoadTrackedAsync(id, cancellationToken);
        var previous = new { Status = order.Status.ToString(), PaymentStatus = order.PaymentStatus.ToString() };

        if (order.Status == target)
        {
            throw new ConflictException($"Siparis zaten '{target}' durumunda.");
        }

        if (!StatusTransitions[order.Status].Contains(target))
        {
            throw new ConflictException($"Siparis '{order.Status}' durumundan '{target}' durumuna gecirilemez.");
        }

        if (target == StoreOrderStatus.Shipped && order.PaymentMethod != StorePaymentMethod.CashOnDelivery && order.PaymentStatus != StorePaymentStatus.Paid)
        {
            throw new ConflictException("Odemesi alinmamis siparis kargoya verilemez. Once odeme durumunu 'Paid' yapin.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (target == StoreOrderStatus.Cancelled)
            {
                Cancel(order, note ?? "Yonetici tarafindan iptal edildi.");
            }
            else
            {
                order.Status = target;
            }

            order.UpdatedAt = UtcNow;
            order.UpdatedUser = adminUserId;
            await AddHistoryAsync(order, adminUserId, note, cancellationToken);
            await SaveOrderAsync(cancellationToken);
            if (target == StoreOrderStatus.Cancelled)
            {
                await ReleaseStockAsync(order, cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _auditLogService.LogAsync(adminUserId, "STORE_ORDER_STATUS_CHANGED", nameof(StoreOrder), id, ipAddress, userAgent,
            previous, new { Status = order.Status.ToString(), PaymentStatus = order.PaymentStatus.ToString(), note }, cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<AdminStoreOrderDto> UpdatePaymentStatusAsync(long id, AdminUpdateStorePaymentStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var target = AdminStoreInput.ParseEnum<StorePaymentStatus>(request.PaymentStatus, nameof(request.PaymentStatus), "Gecersiz odeme durumu.");
        var note = AdminStoreInput.NullIfBlank(request.Note);
        var reference = AdminStoreInput.NullIfBlank(request.PaymentReference);
        var order = await LoadTrackedAsync(id, cancellationToken);
        var current = order.PaymentStatus;
        var previous = new { Status = order.Status.ToString(), PaymentStatus = current.ToString() };

        if (current == target)
        {
            throw new ConflictException($"Odeme zaten '{target}' durumunda.");
        }

        if (!PaymentTransitions[current].Contains(target))
        {
            throw new ConflictException($"Odeme '{current}' durumundan '{target}' durumuna gecirilemez.");
        }

        var cancelsOrder = target == StorePaymentStatus.Failed
            && order.Status is StoreOrderStatus.Received or StoreOrderStatus.Preparing;
        var now = UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Kosullu UPDATE: ayni anda iki farkli odeme sonucu yazilamaz; ikinci istek 409 alir.
            var claimed = await _unitOfWork.Repository<StoreOrder>().GetQueryable(asNoTracking: false)
                .Where(o => o.Id == id && o.PaymentStatus == current)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentStatus, target), cancellationToken);
            if (claimed == 0)
            {
                throw new ConflictException("Odeme durumu ayni anda baska bir islemle degisti. Sayfayi yenileyip tekrar deneyin.");
            }

            order.PaymentStatus = target;
            if (target == StorePaymentStatus.Paid)
            {
                order.PaidAt = now;
            }

            if (reference is not null)
            {
                order.PaymentReference = reference;
            }

            if (cancelsOrder)
            {
                Cancel(order, note ?? "Odeme alinamadigi icin iptal edildi.");
            }

            order.UpdatedAt = now;
            order.UpdatedUser = adminUserId;
            await AddHistoryAsync(order, adminUserId, note, cancellationToken);
            await SaveOrderAsync(cancellationToken);
            if (cancelsOrder)
            {
                await ReleaseStockAsync(order, cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _auditLogService.LogAsync(adminUserId, "STORE_ORDER_PAYMENT_STATUS_CHANGED", nameof(StoreOrder), id, ipAddress, userAgent,
            previous, new { Status = order.Status.ToString(), PaymentStatus = order.PaymentStatus.ToString(), reference, note }, cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    /// <summary>Iptal: odeme alinmissa iade bekliyor, alinmamissa iptal olarak isaretlenir.</summary>
    private void Cancel(StoreOrder order, string reason)
    {
        order.Status = StoreOrderStatus.Cancelled;
        order.CancelledAt = UtcNow;
        order.CancelReason = reason.Length > 300 ? reason[..300] : reason;
        order.PaymentStatus = order.PaymentStatus switch
        {
            StorePaymentStatus.Paid => StorePaymentStatus.RefundPending,
            StorePaymentStatus.Pending => StorePaymentStatus.Cancelled,
            var other => other,
        };
    }

    private Task AddHistoryAsync(StoreOrder order, long? adminUserId, string? note, CancellationToken cancellationToken) =>
        _unitOfWork.Repository<StoreOrderStatusHistory>().AddAsync(new StoreOrderStatusHistory
        {
            OrderId = order.Id,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            ChangedBy = adminUserId.HasValue ? $"admin:{adminUserId.Value}" : "admin",
            Note = note,
            CreatedAt = UtcNow,
            CreatedUser = adminUserId,
        }, cancellationToken);

    /// <summary>Siparisteki urunlerin stogunu geri ekler (silinmis urunler atlanir).</summary>
    private async Task ReleaseStockAsync(StoreOrder order, CancellationToken cancellationToken)
    {
        var products = _unitOfWork.Repository<Product>().GetQueryable(asNoTracking: false);
        foreach (var group in order.Items.Where(i => i.ProductId.HasValue).GroupBy(i => i.ProductId!.Value).OrderBy(g => g.Key))
        {
            var productId = group.Key;
            var quantity = group.Sum(i => i.Quantity);
            await products.Where(p => p.Id == productId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity + quantity), cancellationToken);
        }
    }

    private async Task SaveOrderAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _unitOfWork.ClearTracking();
            throw new ConflictException("Siparis ayni anda baska bir islemle guncellendi. Sayfayi yenileyip tekrar deneyin.");
        }
    }

    private async Task<StoreOrder> LoadTrackedAsync(long id, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<StoreOrder>().GetQueryable(asNoTracking: false)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
        ?? throw new NotFoundException("Siparis", id);

    private static IEnumerable<StoreOrderStatus> AllowedStatuses(StoreOrder order) =>
        StatusTransitions[order.Status].Where(s => s != StoreOrderStatus.Shipped
            || order.PaymentMethod == StorePaymentMethod.CashOnDelivery
            || order.PaymentStatus == StorePaymentStatus.Paid);

    private static T Fill<T>(T dto, StoreOrder o, string? email, int itemCount) where T : AdminStoreOrderListItemDto
    {
        dto.Id = o.Id;
        dto.OrderNumber = o.OrderNumber;
        dto.UserId = o.UserId;
        dto.UserEmail = email;
        dto.RecipientName = ReadAddress(o.ShippingAddressSnapshot).RecipientName;
        dto.Status = o.Status.ToString();
        dto.PaymentStatus = o.PaymentStatus.ToString();
        dto.PaymentMethod = o.PaymentMethod.ToString();
        dto.TotalAmount = o.TotalAmount;
        dto.Currency = o.Currency;
        dto.ItemCount = itemCount;
        dto.CreatedAt = o.CreatedAt;
        return dto;
    }

    private static AdminStoreOrderAddressDto ReadAddress(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AdminStoreOrderAddressDto();
        try
        {
            return JsonSerializer.Deserialize<AdminStoreOrderAddressDto>(json, AdminStoreInput.Json) ?? new AdminStoreOrderAddressDto();
        }
        catch (JsonException)
        {
            return new AdminStoreOrderAddressDto();
        }
    }
}
