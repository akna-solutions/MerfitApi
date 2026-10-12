using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Scores;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>
/// IAdminScoreService'in varsayilan implementasyonu.
///
/// RECALCULATE: Kullanicinin tum zamanlar MB FIT puani MerfitPointsCalculator ile kaynak verilerden
/// (antrenman, kisisel rekor, ogun/su kaydi, basari) yeniden hesaplanir; "AllTime" MerfitScore kaydi,
/// kategori dagilimi (MerfitScoreBreakdown) ve gunluk MerfitScoreHistory kaydi guncellenir. Mobil
/// uygulama API'si (MerfitCustomerApi) her aksiyondan sonra ayni ozeti ayni kurallarla yazar.
/// </summary>
public class AdminScoreService : IAdminScoreService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;
    private readonly MerfitPointsCalculator _pointsCalculator;

    public AdminScoreService(IUnitOfWork unitOfWork, IAuditLogService auditLogService, MerfitPointsCalculator pointsCalculator)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
        _pointsCalculator = pointsCalculator;
    }

    public async Task<PagedResult<AdminScoreListItemDto>> GetListAsync(AdminScoreListRequest request, CancellationToken cancellationToken = default)
    {
        var scores = _unitOfWork.Repository<MerfitScore>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        var latestPerUser = from userId in scores.Select(s => s.UserId).Distinct()
                             let latest = scores.Where(s => s.UserId == userId)
                                 .OrderByDescending(s => s.CalculatedAt)
                                 .FirstOrDefault()
                             join u in users on userId equals u.Id
                             select new
                             {
                                 UserId = userId,
                                 UserEmail = u.Email,
                                 Score = latest != null ? latest.Score : 0,
                                 Period = latest != null ? latest.Period : null,
                                 CalculatedAt = latest != null ? latest.CalculatedAt : (DateTime?)null,
                             };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            latestPerUser = latestPerUser.Where(x => x.UserEmail.Contains(term));
        }

        var ordered = request.IsDescending
            ? latestPerUser.OrderByDescending(x => x.Score)
            : latestPerUser.OrderBy(x => x.Score);

        var totalCount = await latestPerUser.CountAsync(cancellationToken);

        var page = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new AdminScoreListItemDto
            {
                UserId = x.UserId,
                UserEmail = x.UserEmail,
                Score = x.Score,
                Period = x.Period,
                CalculatedAt = x.CalculatedAt,
            })
            .ToListAsync(cancellationToken);

        return PagedResult<AdminScoreListItemDto>.Create(page, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminScoreListItemDto> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        var userExists = await _unitOfWork.Repository<ApplicationUser>().AnyAsync(u => u.Id == userId, cancellationToken);
        if (!userExists)
        {
            throw new NotFoundException(nameof(ApplicationUser), userId);
        }

        var userEmail = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == userId).Select(u => u.Email).FirstAsync(cancellationToken);

        var latest = await _unitOfWork.Repository<MerfitScore>().GetQueryable()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new AdminScoreListItemDto
        {
            UserId = userId,
            UserEmail = userEmail,
            Score = latest?.Score ?? 0,
            Period = latest?.Period,
            CalculatedAt = latest?.CalculatedAt,
        };
    }

    public async Task<AdminRecalculateScoreResultDto> RecalculateAsync(long userId, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var userExists = await _unitOfWork.Repository<ApplicationUser>().AnyAsync(u => u.Id == userId, cancellationToken);
        if (!userExists)
        {
            throw new NotFoundException(nameof(ApplicationUser), userId);
        }

        var previousScore = await _unitOfWork.Repository<MerfitScore>().GetQueryable()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CalculatedAt)
            .Select(s => (decimal?)s.Score)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        var events = await _pointsCalculator.GetPointEventsAsync(null, null, userId, cancellationToken);
        var total = (decimal)events.Sum(e => e.Points);
        var now = DateTime.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var scoreRepo = _unitOfWork.Repository<MerfitScore>();
        var latest = await scoreRepo.GetQueryable(asNoTracking: false)
            .Where(s => s.UserId == userId && s.Period == MerfitPointsCalculator.AllTimePeriodTag)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            latest = new MerfitScore
            {
                UserId = userId,
                Period = MerfitPointsCalculator.AllTimePeriodTag,
                CreatedAt = now,
                CreatedUser = adminUserId,
            };
            await scoreRepo.AddAsync(latest, cancellationToken);
        }
        else
        {
            latest.UpdatedAt = now;
            latest.UpdatedUser = adminUserId;
            scoreRepo.Update(latest);
        }

        latest.Score = total;
        latest.CalculatedAt = now;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var breakdownRepo = _unitOfWork.Repository<MerfitScoreBreakdown>();
        breakdownRepo.RemoveRange(await breakdownRepo.FindAsync(b => b.MerfitScoreId == latest.Id, cancellationToken));
        await breakdownRepo.AddRangeAsync(events
            .GroupBy(e => e.Category)
            .Select(g => new MerfitScoreBreakdown
            {
                MerfitScoreId = latest.Id,
                Category = g.Key,
                Points = g.Sum(e => e.Points),
                CreatedAt = now,
                CreatedUser = adminUserId,
            }), cancellationToken);

        var todayStart = _pointsCalculator.GetLocalDayStartUtc(now);
        var historyRepo = _unitOfWork.Repository<MerfitScoreHistory>();
        var todayHistory = await historyRepo.GetQueryable(asNoTracking: false)
            .Where(h => h.UserId == userId && h.RecordedAt >= todayStart)
            .OrderByDescending(h => h.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (todayHistory is null)
        {
            await historyRepo.AddAsync(new MerfitScoreHistory
            {
                UserId = userId,
                Score = total,
                RecordedAt = now,
                CreatedAt = now,
                CreatedUser = adminUserId,
            }, cancellationToken);
        }
        else
        {
            todayHistory.Score = total;
            todayHistory.RecordedAt = now;
            todayHistory.UpdatedAt = now;
            todayHistory.UpdatedUser = adminUserId;
            historyRepo.Update(todayHistory);
        }

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "SCORE_RECALCULATED", nameof(MerfitScore), latest.Id, ipAddress, userAgent,
            oldValue: new { PreviousScore = previousScore }, newValue: new { NewScore = latest.Score }, cancellationToken: cancellationToken);

        return new AdminRecalculateScoreResultDto
        {
            UserId = userId,
            PreviousScore = previousScore,
            NewScore = latest.Score,
            CalculatedAt = now,
        };
    }
}
