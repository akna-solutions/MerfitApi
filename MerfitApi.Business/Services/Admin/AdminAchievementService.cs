using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Achievements;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminAchievementService'in varsayilan implementasyonu.</summary>
public class AdminAchievementService : IAdminAchievementService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<Achievement, object?>>> SortMap =
        new Dictionary<string, Expression<Func<Achievement, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = x => x.Title,
            ["points"] = x => x.Points,
            ["createdAt"] = x => x.CreatedAt,
        };

    public AdminAchievementService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminAchievementDto>> GetListAsync(AdminAchievementListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Achievement>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Title.Contains(term) || x.Code.Contains(term));
        }

        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);

        var ordered = query.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "createdAt");

        var mapped = ordered.Select(x => new AdminAchievementDto
        {
            Id = x.Id,
            Code = x.Code,
            Title = x.Title,
            Description = x.Description,
            Icon = x.Icon,
            Points = x.Points,
            ConditionType = x.ConditionType,
            ConditionValue = x.ConditionValue,
            IsActive = x.IsActive,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
        });

        return await mapped.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<AdminAchievementDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => MapToDto(await GetOrThrowAsync(id, cancellationToken));

    public async Task<AdminAchievementDto> CreateAsync(AdminUpsertAchievementRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Achievement>();

        if (await repo.AnyAsync(x => x.Code == request.Code, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Code), "Bu kod zaten kullaniliyor.");
        }

        var entity = new Achievement
        {
            Code = request.Code.Trim().ToUpperInvariant(),
            Title = request.Title.Trim(),
            Description = request.Description,
            Icon = request.Icon,
            Points = request.Points,
            ConditionType = request.ConditionType.Trim(),
            ConditionValue = request.ConditionValue.Trim(),
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await repo.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "ACHIEVEMENT_CREATED", nameof(Achievement), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return MapToDto(entity);
    }

    public async Task<AdminAchievementDto> UpdateAsync(long id, AdminUpsertAchievementRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Achievement>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Achievement), id);

        if (await repo.AnyAsync(x => x.Code == request.Code && x.Id != id, cancellationToken))
        {
            throw new AppValidationException(nameof(request.Code), "Bu kod zaten kullaniliyor.");
        }

        var oldValue = new { entity.Code, entity.Title, entity.Points, entity.IsActive };

        entity.Code = request.Code.Trim().ToUpperInvariant();
        entity.Title = request.Title.Trim();
        entity.Description = request.Description;
        entity.Icon = request.Icon;
        entity.Points = request.Points;
        entity.ConditionType = request.ConditionType.Trim();
        entity.ConditionValue = request.ConditionValue.Trim();
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "ACHIEVEMENT_UPDATED", nameof(Achievement), id, ipAddress, userAgent,
            oldValue, new { entity.Code, entity.Title, entity.Points, entity.IsActive }, cancellationToken);

        return MapToDto(entity);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Achievement>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Achievement), id);

        var isEarned = await _unitOfWork.Repository<UserAchievement>().AnyAsync(x => x.AchievementId == id, cancellationToken);
        if (isEarned)
        {
            throw new ConflictException("Bu basarim en az bir kullanici tarafindan kazanildigi icin silinemez. Bunun yerine pasif hale getirebilirsiniz (PATCH /status).");
        }

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "ACHIEVEMENT_DELETED", nameof(Achievement), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateAchievementStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Achievement>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Achievement), id);

        var oldValue = new { entity.IsActive };
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "ACHIEVEMENT_STATUS_CHANGED", nameof(Achievement), id, ipAddress, userAgent, oldValue, new { entity.IsActive }, cancellationToken);
    }

    public async Task<PagedResult<AdminAchievementUserListItemDto>> GetUsersAsync(long id, PagedRequest request, CancellationToken cancellationToken = default)
    {
        await GetOrThrowAsync(id, cancellationToken);

        var userAchievements = _unitOfWork.Repository<UserAchievement>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        var query = from ua in userAchievements
                     where ua.AchievementId == id
                     join u in users on ua.UserId equals u.Id
                     orderby ua.EarnedAt descending
                     select new AdminAchievementUserListItemDto
                     {
                         UserId = u.Id,
                         UserEmail = u.Email,
                         EarnedAt = ua.EarnedAt,
                     };

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    private async Task<Achievement> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Achievement>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Achievement), id);
    }

    private static AdminAchievementDto MapToDto(Achievement entity) => new()
    {
        Id = entity.Id,
        Code = entity.Code,
        Title = entity.Title,
        Description = entity.Description,
        Icon = entity.Icon,
        Points = entity.Points,
        ConditionType = entity.ConditionType,
        ConditionValue = entity.ConditionValue,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };
}
