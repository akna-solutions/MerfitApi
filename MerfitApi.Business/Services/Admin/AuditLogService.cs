using System.Linq.Expressions;
using System.Text.Json;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Audit;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MerfitApi.Business.Services.Admin;

/// <summary>
/// IAuditLogService'in varsayilan implementasyonu. AuditLog kayitlarini yazar ve
/// admin panelinde listeleme/detay goruntuleme icin sorgular.
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AuditLogService> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private static readonly IReadOnlyDictionary<string, Expression<Func<AuditLog, object?>>> SortMap =
        new Dictionary<string, Expression<Func<AuditLog, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = x => x.CreatedAt,
            ["action"] = x => x.Action,
            ["entity"] = x => x.Entity,
        };

    public AuditLogService(IUnitOfWork unitOfWork, ILogger<AuditLogService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task LogAsync(
        long? adminUserId,
        string action,
        string entity,
        long? entityId,
        string? ipAddress,
        string? userAgent,
        object? oldValue = null,
        object? newValue = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            object? metadata = null;
            if (oldValue is not null || newValue is not null)
            {
                metadata = new { oldValue, newValue };
            }

            var log = new AuditLog
            {
                UserId = adminUserId,
                Action = action,
                Entity = entity,
                EntityId = entityId,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                MetadataJson = metadata is null
    ? null
    : JsonDocument.Parse(JsonSerializer.Serialize(metadata, SerializerOptions)),
                CreatedAt = DateTime.UtcNow,
            };

            await _unitOfWork.Repository<AuditLog>().AddAsync(log, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit log yazimi basarisiz olsa dahi ana is akisini (asil admin islemini) engellememeli;
            // sadece loglanir. Bu davranis bilincli bir tasarim tercihidir.
            _logger.LogError(ex, "Audit log yazilamadi. Action: {Action}, Entity: {Entity}, EntityId: {EntityId}", action, entity, entityId);
        }
    }

    public async Task<PagedResult<AdminAuditLogListItemDto>> GetListAsync(AdminAuditLogListRequest request, CancellationToken cancellationToken = default)
    {
        var logsQuery = _unitOfWork.Repository<AuditLog>().GetQueryable();
        var usersQuery = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        if (request.AdminUserId.HasValue)
        {
            logsQuery = logsQuery.Where(x => x.UserId == request.AdminUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            logsQuery = logsQuery.Where(x => x.Action.Contains(request.Action));
        }

        if (!string.IsNullOrWhiteSpace(request.Entity))
        {
            logsQuery = logsQuery.Where(x => x.Entity == request.Entity);
        }

        if (request.EntityId.HasValue)
        {
            logsQuery = logsQuery.Where(x => x.EntityId == request.EntityId.Value);
        }

        if (request.From.HasValue)
        {
            logsQuery = logsQuery.Where(x => x.CreatedAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            logsQuery = logsQuery.Where(x => x.CreatedAt <= request.To.Value);
        }

        var projected = from log in logsQuery
                         join user in usersQuery on log.UserId equals user.Id into userJoin
                         from user in userJoin.DefaultIfEmpty()
                         select new AdminAuditLogListItemDto
                         {
                             Id = log.Id,
                             AdminUserId = log.UserId,
                             AdminEmail = user != null ? user.Email : null,
                             Action = log.Action,
                             Entity = log.Entity,
                             EntityId = log.EntityId,
                             IpAddress = log.IpAddress,
                             CreatedAt = log.CreatedAt,
                         };

        // ApplySort, AuditLog entity'si uzerinde calisan whitelist'i kullaniyor; projeksiyon oncesi
        // siralamak icin logsQuery uzerinde OrderBy uygulanip sonra join/projeksiyon yapmak daha dogru
        // oldugundan siralamayi burada logsQuery uzerinden yeniden kuruyoruz.
        var orderedLogs = logsQuery.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "createdAt");

        var orderedProjection = from log in orderedLogs
                                 join user in usersQuery on log.UserId equals user.Id into userJoin
                                 from user in userJoin.DefaultIfEmpty()
                                 select new AdminAuditLogListItemDto
                                 {
                                     Id = log.Id,
                                     AdminUserId = log.UserId,
                                     AdminEmail = user != null ? user.Email : null,
                                     Action = log.Action,
                                     Entity = log.Entity,
                                     EntityId = log.EntityId,
                                     IpAddress = log.IpAddress,
                                     CreatedAt = log.CreatedAt,
                                 };

        return await orderedProjection.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<AdminAuditLogDetailDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var log = await _unitOfWork.Repository<AuditLog>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(AuditLog), id);

        string? adminEmail = null;
        if (log.UserId.HasValue)
        {
            var admin = await _unitOfWork.Repository<ApplicationUser>().GetByIdAsync(log.UserId.Value, cancellationToken);
            adminEmail = admin?.Email;
        }

        string? oldValueJson = null;
        string? newValueJson = null;

        if (log.MetadataJson is not null)
        {
            var root = log.MetadataJson.RootElement;
            if (root.TryGetProperty("oldValue", out var oldValueElement))
            {
                oldValueJson = oldValueElement.ValueKind == JsonValueKind.Null ? null : oldValueElement.GetRawText();
            }

            if (root.TryGetProperty("newValue", out var newValueElement))
            {
                newValueJson = newValueElement.ValueKind == JsonValueKind.Null ? null : newValueElement.GetRawText();
            }
        }

        return new AdminAuditLogDetailDto
        {
            Id = log.Id,
            AdminUserId = log.UserId,
            AdminEmail = adminEmail,
            Action = log.Action,
            Entity = log.Entity,
            EntityId = log.EntityId,
            IpAddress = log.IpAddress,
            UserAgent = log.UserAgent,
            CreatedAt = log.CreatedAt,
            OldValueJson = oldValueJson,
            NewValueJson = newValueJson,
        };
    }
}
