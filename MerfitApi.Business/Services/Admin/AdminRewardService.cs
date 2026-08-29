using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Rewards;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminRewardService'in varsayilan implementasyonu.</summary>
public class AdminRewardService : IAdminRewardService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<Reward, object?>>> SortMap =
        new Dictionary<string, Expression<Func<Reward, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = x => x.Title,
            ["createdAt"] = x => x.CreatedAt,
        };

    public AdminRewardService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminRewardDto>> GetListAsync(AdminRewardListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Reward>().GetQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Title.Contains(term));
        }

        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(request.RewardType)) query = query.Where(x => x.RewardType == request.RewardType);

        var ordered = query.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "createdAt");

        var mapped = ordered.Select(x => new AdminRewardDto
        {
            Id = x.Id,
            Title = x.Title,
            Description = x.Description,
            ImageUrl = x.ImageUrl,
            RewardType = x.RewardType,
            Value = x.Value,
            IsActive = x.IsActive,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
        });

        return await mapped.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<AdminRewardDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => MapToDto(await GetOrThrowAsync(id, cancellationToken));

    public async Task<AdminRewardDto> CreateAsync(AdminUpsertRewardRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = new Reward
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            ImageUrl = request.ImageUrl,
            RewardType = request.RewardType.Trim(),
            Value = request.Value,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.Repository<Reward>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "REWARD_CREATED", nameof(Reward), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return MapToDto(entity);
    }

    public async Task<AdminRewardDto> UpdateAsync(long id, AdminUpsertRewardRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Reward>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Reward), id);

        var oldValue = new { entity.Title, entity.RewardType, entity.IsActive };

        entity.Title = request.Title.Trim();
        entity.Description = request.Description;
        entity.ImageUrl = request.ImageUrl;
        entity.RewardType = request.RewardType.Trim();
        entity.Value = request.Value;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "REWARD_UPDATED", nameof(Reward), id, ipAddress, userAgent, oldValue, new { entity.Title, entity.RewardType, entity.IsActive }, cancellationToken);

        return MapToDto(entity);
    }

    public async Task DeleteAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Reward>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Reward), id);

        var isUsedByLeaderboard = await _unitOfWork.Repository<LeaderboardReward>().AnyAsync(x => x.RewardId == id, cancellationToken);
        var isClaimedByUser = await _unitOfWork.Repository<UserReward>().AnyAsync(x => x.RewardId == id, cancellationToken);

        if (isUsedByLeaderboard || isClaimedByUser)
        {
            throw new ConflictException("Bu odul liderlik tablosu tanimlarinda veya kullanici kazanimlarinda kullanildigi icin silinemez. Bunun yerine pasif hale getirebilirsiniz (PATCH /status).");
        }

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "REWARD_DELETED", nameof(Reward), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    public async Task UpdateStatusAsync(long id, AdminUpdateRewardStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<Reward>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Reward), id);

        var oldValue = new { entity.IsActive };
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "REWARD_STATUS_CHANGED", nameof(Reward), id, ipAddress, userAgent, oldValue, new { entity.IsActive }, cancellationToken);
    }

    public async Task<PagedResult<AdminLeaderboardRewardDto>> GetLeaderboardRewardsAsync(AdminLeaderboardRewardListRequest request, CancellationToken cancellationToken = default)
    {
        var links = _unitOfWork.Repository<LeaderboardReward>().GetQueryable();
        var rewards = _unitOfWork.Repository<Reward>().GetQueryable();

        var query = links.AsQueryable();
        if (request.LeaderboardPeriodId.HasValue)
        {
            query = query.Where(x => x.LeaderboardPeriodId == request.LeaderboardPeriodId.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        var page = await query
            .OrderBy(x => x.LeaderboardPeriodId).ThenBy(x => x.Rank)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new AdminLeaderboardRewardDto
            {
                Id = x.Id,
                LeaderboardPeriodId = x.LeaderboardPeriodId,
                Rank = x.Rank,
                RewardId = x.RewardId,
                RewardTitle = rewards.Where(r => r.Id == x.RewardId).Select(r => r.Title).FirstOrDefault() ?? "-",
            })
            .ToListAsync(cancellationToken);

        return PagedResult<AdminLeaderboardRewardDto>.Create(page, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminLeaderboardRewardDto> CreateLeaderboardRewardAsync(AdminUpsertLeaderboardRewardRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateLeaderboardRewardAsync(request, cancellationToken);

        var entity = new LeaderboardReward
        {
            LeaderboardPeriodId = request.LeaderboardPeriodId,
            Rank = request.Rank,
            RewardId = request.RewardId,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.Repository<LeaderboardReward>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LEADERBOARD_REWARD_CREATED", nameof(LeaderboardReward), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return await MapLeaderboardRewardToDtoAsync(entity, cancellationToken);
    }

    public async Task<AdminLeaderboardRewardDto> UpdateLeaderboardRewardAsync(long id, AdminUpsertLeaderboardRewardRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateLeaderboardRewardAsync(request, cancellationToken);

        var repo = _unitOfWork.Repository<LeaderboardReward>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaderboardReward), id);

        var oldValue = new { entity.LeaderboardPeriodId, entity.Rank, entity.RewardId };

        entity.LeaderboardPeriodId = request.LeaderboardPeriodId;
        entity.Rank = request.Rank;
        entity.RewardId = request.RewardId;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LEADERBOARD_REWARD_UPDATED", nameof(LeaderboardReward), id, ipAddress, userAgent,
            oldValue, new { entity.LeaderboardPeriodId, entity.Rank, entity.RewardId }, cancellationToken);

        return await MapLeaderboardRewardToDtoAsync(entity, cancellationToken);
    }

    public async Task DeleteLeaderboardRewardAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<LeaderboardReward>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaderboardReward), id);

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LEADERBOARD_REWARD_DELETED", nameof(LeaderboardReward), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    private async Task ValidateLeaderboardRewardAsync(AdminUpsertLeaderboardRewardRequest request, CancellationToken cancellationToken)
    {
        var periodExists = await _unitOfWork.Repository<LeaderboardPeriod>().AnyAsync(x => x.Id == request.LeaderboardPeriodId, cancellationToken);
        if (!periodExists)
        {
            throw new AppValidationException(nameof(request.LeaderboardPeriodId), "Belirtilen liderlik donemi bulunamadi.");
        }

        var rewardExists = await _unitOfWork.Repository<Reward>().AnyAsync(x => x.Id == request.RewardId, cancellationToken);
        if (!rewardExists)
        {
            throw new AppValidationException(nameof(request.RewardId), "Belirtilen odul bulunamadi.");
        }
    }

    private async Task<AdminLeaderboardRewardDto> MapLeaderboardRewardToDtoAsync(LeaderboardReward entity, CancellationToken cancellationToken)
    {
        var rewardTitle = await _unitOfWork.Repository<Reward>().GetQueryable()
            .Where(r => r.Id == entity.RewardId).Select(r => r.Title).FirstOrDefaultAsync(cancellationToken);

        return new AdminLeaderboardRewardDto
        {
            Id = entity.Id,
            LeaderboardPeriodId = entity.LeaderboardPeriodId,
            Rank = entity.Rank,
            RewardId = entity.RewardId,
            RewardTitle = rewardTitle ?? "-",
        };
    }

    private async Task<Reward> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Reward>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Reward), id);
    }

    private static AdminRewardDto MapToDto(Reward entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Description = entity.Description,
        ImageUrl = entity.ImageUrl,
        RewardType = entity.RewardType,
        Value = entity.Value,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };
}
