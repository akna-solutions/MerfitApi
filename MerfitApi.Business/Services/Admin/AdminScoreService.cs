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
/// RECALCULATE VARSAYIMI (madde 22): Repository icinde skorun nasil hesaplandigina dair bir
/// algoritma/servis bulunmuyor (workout/streak/nutrition gibi kaynaklardan puan turetimi ayri bir
/// is kuralidir ve bu depoda tanimli degildir). Bu nedenle "recalculate", MEVCUT en guncel
/// MerfitScoreBreakdown kayitlarinin toplamini yeniden Score alanina yazan ve bir
/// MerfitScoreHistory kaydi olusturan guvenli, veri kaybettirmeyen bir islem olarak
/// yorumlanmistir. Gercek puanlama algoritmasi eklendiginde, bu metodun ic gorunumu
/// degistirilip disaridan ayni sozlesme (recalculate) korunabilir.
/// </summary>
public class AdminScoreService : IAdminScoreService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminScoreService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
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

        var scoreRepo = _unitOfWork.Repository<MerfitScore>();
        var latest = await scoreRepo.GetQueryable(asNoTracking: false)
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var previousScore = latest?.Score ?? 0;
        var now = DateTime.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        if (latest is null)
        {
            // Kullanicinin hic skoru yoksa, sifir puanla baslangic kaydi olusturulur.
            latest = new MerfitScore
            {
                UserId = userId,
                Score = 0,
                Period = "manual-recalculation",
                CalculatedAt = now,
                CreatedAt = now,
                CreatedUser = adminUserId,
            };
            await scoreRepo.AddAsync(latest, cancellationToken);
        }
        else
        {
            var breakdownTotal = await _unitOfWork.Repository<MerfitScoreBreakdown>().GetQueryable()
                .Where(b => b.MerfitScoreId == latest.Id)
                .SumAsync(b => (decimal?)b.Points, cancellationToken) ?? 0m;

            latest.Score = breakdownTotal;
            latest.CalculatedAt = now;
            latest.UpdatedAt = now;
            latest.UpdatedUser = adminUserId;
            scoreRepo.Update(latest);
        }

        await _unitOfWork.Repository<MerfitScoreHistory>().AddAsync(new MerfitScoreHistory
        {
            UserId = userId,
            Score = latest.Score,
            RecordedAt = now,
            CreatedAt = now,
            CreatedUser = adminUserId,
        }, cancellationToken);

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
