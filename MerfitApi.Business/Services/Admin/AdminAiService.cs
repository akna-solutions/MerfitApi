using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Ai;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminAiService'in varsayilan implementasyonu. Tamamen salt-okunurdur.</summary>
public class AdminAiService : IAdminAiService
{
    private readonly IUnitOfWork _unitOfWork;

    public AdminAiService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<AdminAiRequestListItemDto>> GetRequestsAsync(AdminAiRequestListRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<AiGenerationRequest>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        if (request.UserId.HasValue) query = query.Where(x => x.UserId == request.UserId.Value);
        if (request.Type.HasValue) query = query.Where(x => x.Type == request.Type.Value);
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
        if (!string.IsNullOrWhiteSpace(request.Model)) query = query.Where(x => x.Model == request.Model);
        if (request.From.HasValue) query = query.Where(x => x.CreatedAt >= request.From.Value);
        if (request.To.HasValue) query = query.Where(x => x.CreatedAt <= request.To.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);

        var raw = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.Type,
                x.Status,
                x.Model,
                x.StartedAt,
                x.CompletedAt,
                x.CreatedAt,
                UserEmail = users.Where(u => u.Id == x.UserId).Select(u => u.Email).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = raw.Select(x => new AdminAiRequestListItemDto
        {
            Id = x.Id,
            UserId = x.UserId,
            UserEmail = x.UserEmail ?? "-",
            Type = x.Type.ToString(),
            Status = x.Status.ToString(),
            Model = x.Model,
            StartedAt = x.StartedAt,
            CompletedAt = x.CompletedAt,
            CreatedAt = x.CreatedAt,
        }).ToList();

        return PagedResult<AdminAiRequestListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminAiRequestDetailDto> GetRequestByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<AiGenerationRequest>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(AiGenerationRequest), id);

        var userEmail = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == entity.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        var hasResult = await _unitOfWork.Repository<AiGenerationResult>().AnyAsync(r => r.RequestId == id, cancellationToken);

        return new AdminAiRequestDetailDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            UserEmail = userEmail ?? "-",
            Type = entity.Type.ToString(),
            Status = entity.Status.ToString(),
            Model = entity.Model,
            StartedAt = entity.StartedAt,
            CompletedAt = entity.CompletedAt,
            CreatedAt = entity.CreatedAt,
            Prompt = entity.Prompt,
            HasResult = hasResult,
        };
    }

    public async Task<PagedResult<AdminAiResultListItemDto>> GetResultsAsync(AdminAiResultListRequest request, CancellationToken cancellationToken = default)
    {
        var results = _unitOfWork.Repository<AiGenerationResult>().GetQueryable();
        var requests = _unitOfWork.Repository<AiGenerationRequest>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();

        var query = results.AsQueryable();
        if (request.RequestId.HasValue) query = query.Where(x => x.RequestId == request.RequestId.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);

        var raw = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Id,
                x.RequestId,
                x.CreatedAt,
                UserId = requests.Where(r => r.Id == x.RequestId).Select(r => r.UserId).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var userIds = raw.Select(x => x.UserId).Distinct().ToList();
        var userEmails = await users.Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.Email }).ToListAsync(cancellationToken);

        var items = raw.Select(x => new AdminAiResultListItemDto
        {
            Id = x.Id,
            RequestId = x.RequestId,
            UserId = x.UserId,
            UserEmail = userEmails.FirstOrDefault(u => u.Id == x.UserId)?.Email ?? "-",
            CreatedAt = x.CreatedAt,
        }).ToList();

        return PagedResult<AdminAiResultListItemDto>.Create(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminAiResultDetailDto> GetResultByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<AiGenerationResult>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(AiGenerationResult), id);

        var relatedRequest = await _unitOfWork.Repository<AiGenerationRequest>().GetByIdAsync(entity.RequestId, cancellationToken);

        var userEmail = relatedRequest is null
            ? null
            : await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
                .Where(u => u.Id == relatedRequest.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        return new AdminAiResultDetailDto
        {
            Id = entity.Id,
            RequestId = entity.RequestId,
            UserId = relatedRequest?.UserId ?? 0,
            UserEmail = userEmail ?? "-",
            CreatedAt = entity.CreatedAt,
            ResultJson = entity.ResultJson,
        };
    }

    public async Task<AdminAiStatisticsDto> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var requests = _unitOfWork.Repository<AiGenerationRequest>().GetQueryable();

        var totalRequests = await requests.LongCountAsync(cancellationToken);
        var successfulRequests = await requests.LongCountAsync(x => x.Status == AiGenerationStatus.Completed, cancellationToken);
        var failedRequests = await requests.LongCountAsync(x => x.Status == AiGenerationStatus.Failed, cancellationToken);
        var pendingRequests = await requests.LongCountAsync(
            x => x.Status == AiGenerationStatus.Pending || x.Status == AiGenerationStatus.Processing, cancellationToken);

        // Ortalama sure, TimeSpan farkinin SQL tarafinda guvenilir sekilde AVG'lenmesi provider'a gore
        // degisebildiginden, tamamlanmis isteklerin StartedAt/CompletedAt ciftleri bellek tarafina
        // cekilip orada hesaplanir (kayit sayisi tipik olarak analiz icin makul buyuklukte olur).
        var completedDurations = await requests
            .Where(x => x.StartedAt != null && x.CompletedAt != null)
            .Select(x => new { x.StartedAt, x.CompletedAt })
            .ToListAsync(cancellationToken);

        var averageDurationSeconds = completedDurations.Count == 0
            ? 0
            : completedDurations.Average(x => (x.CompletedAt!.Value - x.StartedAt!.Value).TotalSeconds);

        var modelDistribution = await requests
            .Where(x => x.Model != null)
            .GroupBy(x => x.Model)
            .Select(g => new { Model = g.Key, Count = g.LongCount() })
            .ToListAsync(cancellationToken);

        var typeDistribution = await requests
            .GroupBy(x => x.Type)
            .Select(g => new { Type = g.Key, Count = g.LongCount() })
            .ToListAsync(cancellationToken);

        return new AdminAiStatisticsDto
        {
            TotalRequests = totalRequests,
            SuccessfulRequests = successfulRequests,
            FailedRequests = failedRequests,
            PendingRequests = pendingRequests,
            AverageDurationSeconds = Math.Round(averageDurationSeconds, 2),
            ModelDistribution = modelDistribution
                .Select(x => new AdminAiDistributionItemDto { Key = x.Model ?? "-", Count = x.Count })
                .ToList(),
            RequestTypeDistribution = typeDistribution
                .Select(x => new AdminAiDistributionItemDto { Key = x.Type.ToString(), Count = x.Count })
                .ToList(),
        };
    }
}
