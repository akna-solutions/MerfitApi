using System.Linq.Expressions;
using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Leaderboards;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>
/// IAdminLeaderboardService'in varsayilan implementasyonu.
///
/// PUANLAMA VARSAYIMI (madde 23): Repository icinde liderlik puanlamasinin nasil hesaplandigina
/// dair bir algoritma tanimli degildir. Bu servis, "recalculate" islemini donem tarih araliginda
/// (StartDate-EndDate) TAMAMLANMIS antrenman oturumu (WorkoutSession.CompletedAt) SAYISI uzerinden
/// yapar: Points = WorkoutCount = o araliktaki tamamlanmis oturum sayisi. Siralama (Rank) puana
/// gore azalan sirada, esitliklerde kullanici Id'sine gore tutarli bicimde atanir. Gercek is
/// kurali (orn. MerfitScore agirlikli puanlama) eklendiginde bu metodun ic gorunumu degistirilip
/// disaridan ayni sozlesme (recalculate) korunabilir.
/// </summary>
public class AdminLeaderboardService : IAdminLeaderboardService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    private static readonly IReadOnlyDictionary<string, Expression<Func<LeaderboardPeriod, object?>>> SortMap =
        new Dictionary<string, Expression<Func<LeaderboardPeriod, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["startDate"] = x => x.StartDate,
            ["createdAt"] = x => x.CreatedAt,
        };

    public AdminLeaderboardService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task<PagedResult<AdminLeaderboardPeriodDto>> GetPeriodsAsync(AdminLeaderboardPeriodListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<LeaderboardPeriod>().GetQueryable();

        if (request.Type.HasValue) query = query.Where(x => x.Type == request.Type.Value);
        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var ordered = query.ApplySort(request.SortBy, request.IsDescending, SortMap, defaultKey: "startDate");

        var raw = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = raw.Select(MapToDto).ToList();

        return PagedResult<AdminLeaderboardPeriodDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminLeaderboardPeriodDto> GetPeriodByIdAsync(long id, CancellationToken cancellationToken = default)
        => MapToDto(await GetOrThrowAsync(id, cancellationToken));

    public async Task<AdminLeaderboardPeriodDto> CreatePeriodAsync(AdminUpsertLeaderboardPeriodRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        ValidateDateRange(request);

        var entity = new LeaderboardPeriod
        {
            Type = request.Type,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.Repository<LeaderboardPeriod>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LEADERBOARD_PERIOD_CREATED", nameof(LeaderboardPeriod), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return MapToDto(entity);
    }

    public async Task<AdminLeaderboardPeriodDto> UpdatePeriodAsync(long id, AdminUpsertLeaderboardPeriodRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        ValidateDateRange(request);

        var repo = _unitOfWork.Repository<LeaderboardPeriod>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaderboardPeriod), id);

        var oldValue = new { entity.Type, entity.StartDate, entity.EndDate, entity.IsActive };

        entity.Type = request.Type;
        entity.StartDate = request.StartDate;
        entity.EndDate = request.EndDate;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LEADERBOARD_PERIOD_UPDATED", nameof(LeaderboardPeriod), id, ipAddress, userAgent,
            oldValue, new { entity.Type, entity.StartDate, entity.EndDate, entity.IsActive }, cancellationToken);

        return MapToDto(entity);
    }

    public async Task DeletePeriodAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<LeaderboardPeriod>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaderboardPeriod), id);

        var hasRewards = await _unitOfWork.Repository<LeaderboardReward>().AnyAsync(x => x.LeaderboardPeriodId == id, cancellationToken);
        if (hasRewards)
        {
            throw new ConflictException("Bu doneme bagli odul tanimlari oldugu icin silinemez. Once ilgili odul iliskilerini kaldirin.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var entries = await _unitOfWork.Repository<LeaderboardEntry>().FindAsync(x => x.LeaderboardPeriodId == id, cancellationToken);
        _unitOfWork.Repository<LeaderboardEntry>().RemoveRange(entries);

        repo.Remove(entity);

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "LEADERBOARD_PERIOD_DELETED", nameof(LeaderboardPeriod), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    public async Task<PagedResult<AdminLeaderboardEntryDto>> GetEntriesAsync(long periodId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        await GetOrThrowAsync(periodId, cancellationToken);

        var entries = _unitOfWork.Repository<LeaderboardEntry>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        var query = from e in entries
                     where e.LeaderboardPeriodId == periodId
                     join u in users on e.UserId equals u.Id
                     orderby e.Rank
                     select new AdminLeaderboardEntryDto
                     {
                         UserId = u.Id,
                         UserEmail = u.Email,
                         Points = e.Points,
                         WorkoutCount = e.WorkoutCount,
                         Rank = e.Rank,
                     };

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<AdminLeaderboardRecalculateResultDto> RecalculateAsync(long periodId, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var period = await GetOrThrowAsync(periodId, cancellationToken);

        var completedCounts = await _unitOfWork.Repository<WorkoutSession>().GetQueryable()
            .Where(s => s.CompletedAt != null && s.CompletedAt >= period.StartDate && s.CompletedAt <= period.EndDate)
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var ranked = completedCounts
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.UserId)
            .Select((x, index) => new LeaderboardEntry
            {
                LeaderboardPeriodId = periodId,
                UserId = x.UserId,
                Points = x.Count,
                WorkoutCount = x.Count,
                Rank = index + 1,
                CreatedAt = DateTime.UtcNow,
                CreatedUser = adminUserId,
            })
            .ToList();

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var repo = _unitOfWork.Repository<LeaderboardEntry>();
        var existing = await repo.FindAsync(x => x.LeaderboardPeriodId == periodId, cancellationToken);
        repo.RemoveRange(existing);

        if (ranked.Count > 0)
        {
            await repo.AddRangeAsync(ranked, cancellationToken);
        }

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;

        await _auditLogService.LogAsync(adminUserId, "LEADERBOARD_RECALCULATED", nameof(LeaderboardPeriod), periodId, ipAddress, userAgent,
            newValue: new { EntryCount = ranked.Count }, cancellationToken: cancellationToken);

        return new AdminLeaderboardRecalculateResultDto
        {
            LeaderboardPeriodId = periodId,
            EntryCount = ranked.Count,
            RecalculatedAt = now,
        };
    }

    private static void ValidateDateRange(AdminUpsertLeaderboardPeriodRequest request)
    {
        if (request.EndDate <= request.StartDate)
        {
            throw new AppValidationException(nameof(request.EndDate), "Bitis tarihi baslangic tarihinden sonra olmalidir.");
        }
    }

    private async Task<LeaderboardPeriod> GetOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<LeaderboardPeriod>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaderboardPeriod), id);
    }

    private static AdminLeaderboardPeriodDto MapToDto(LeaderboardPeriod entity) => new()
    {
        Id = entity.Id,
        Type = entity.Type.ToString(),
        StartDate = entity.StartDate,
        EndDate = entity.EndDate,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
    };
}
