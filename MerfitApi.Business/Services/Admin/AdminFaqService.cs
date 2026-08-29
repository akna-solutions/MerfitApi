using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Faq;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminFaqService'in varsayilan implementasyonu.</summary>
public class AdminFaqService : IAdminFaqService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminFaqService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    // ---------- Categories ----------

    public async Task<PagedResult<AdminFaqCategoryDto>> GetCategoriesAsync(AdminFaqCategoryListRequest request, CancellationToken cancellationToken = default)
    {
        var categories = _unitOfWork.Repository<FAQCategory>().GetQueryable();
        var faqs = _unitOfWork.Repository<FAQ>().GetQueryable();

        var query = categories.AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Name.Contains(term));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        var page = await query
            .OrderBy(x => x.SortOrder)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new AdminFaqCategoryDto
            {
                Id = x.Id,
                Name = x.Name,
                SortOrder = x.SortOrder,
                FaqCount = faqs.Count(f => f.CategoryId == x.Id),
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return PagedResult<AdminFaqCategoryDto>.Create(page, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminFaqCategoryDto> GetCategoryByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await GetCategoryOrThrowAsync(id, cancellationToken);
        var faqCount = await _unitOfWork.Repository<FAQ>().GetQueryable().CountAsync(f => f.CategoryId == id, cancellationToken);
        return MapCategoryToDto(entity, faqCount);
    }

    public async Task<AdminFaqCategoryDto> CreateCategoryAsync(AdminUpsertFaqCategoryRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var entity = new FAQCategory
        {
            Name = request.Name.Trim(),
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.Repository<FAQCategory>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FAQ_CATEGORY_CREATED", nameof(FAQCategory), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return MapCategoryToDto(entity, 0);
    }

    public async Task<AdminFaqCategoryDto> UpdateCategoryAsync(long id, AdminUpsertFaqCategoryRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<FAQCategory>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(FAQCategory), id);

        var oldValue = new { entity.Name, entity.SortOrder };

        entity.Name = request.Name.Trim();
        entity.SortOrder = request.SortOrder;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FAQ_CATEGORY_UPDATED", nameof(FAQCategory), id, ipAddress, userAgent, oldValue, new { entity.Name, entity.SortOrder }, cancellationToken);

        var faqCount = await _unitOfWork.Repository<FAQ>().GetQueryable().CountAsync(f => f.CategoryId == id, cancellationToken);
        return MapCategoryToDto(entity, faqCount);
    }

    public async Task DeleteCategoryAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<FAQCategory>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(FAQCategory), id);

        var hasFaqs = await _unitOfWork.Repository<FAQ>().AnyAsync(x => x.CategoryId == id, cancellationToken);
        if (hasFaqs)
        {
            throw new ConflictException("Bu kategoriye bagli SSS kayitlari oldugu icin silinemez. Once ilgili SSS'leri baska bir kategoriye tasiyin veya silin.");
        }

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FAQ_CATEGORY_DELETED", nameof(FAQCategory), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    // ---------- FAQs ----------

    public async Task<PagedResult<AdminFaqDto>> GetFaqsAsync(AdminFaqListRequest request, CancellationToken cancellationToken = default)
    {
        var faqs = _unitOfWork.Repository<FAQ>().GetQueryable();
        var categories = _unitOfWork.Repository<FAQCategory>().GetQueryable();

        if (request.CategoryId.HasValue) faqs = faqs.Where(x => x.CategoryId == request.CategoryId.Value);
        if (request.IsActive.HasValue) faqs = faqs.Where(x => x.IsActive == request.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            faqs = faqs.Where(x => x.Question.Contains(term) || x.Answer.Contains(term));
        }

        var totalCount = await faqs.LongCountAsync(cancellationToken);

        var page = await faqs
            .OrderBy(x => x.CategoryId).ThenBy(x => x.SortOrder)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new AdminFaqDto
            {
                Id = x.Id,
                CategoryId = x.CategoryId,
                CategoryName = categories.Where(c => c.Id == x.CategoryId).Select(c => c.Name).FirstOrDefault() ?? "-",
                Question = x.Question,
                Answer = x.Answer,
                SortOrder = x.SortOrder,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return PagedResult<AdminFaqDto>.Create(page, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminFaqDto> GetFaqByIdAsync(long id, CancellationToken cancellationToken = default)
        => await MapFaqToDtoAsync(await GetFaqOrThrowAsync(id, cancellationToken), cancellationToken);

    public async Task<AdminFaqDto> CreateFaqAsync(AdminUpsertFaqRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateCategoryAsync(request.CategoryId, cancellationToken);

        var entity = new FAQ
        {
            CategoryId = request.CategoryId,
            Question = request.Question.Trim(),
            Answer = request.Answer.Trim(),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedUser = adminUserId,
        };

        await _unitOfWork.Repository<FAQ>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FAQ_CREATED", nameof(FAQ), entity.Id, ipAddress, userAgent, newValue: entity, cancellationToken: cancellationToken);

        return await MapFaqToDtoAsync(entity, cancellationToken);
    }

    public async Task<AdminFaqDto> UpdateFaqAsync(long id, AdminUpsertFaqRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await ValidateCategoryAsync(request.CategoryId, cancellationToken);

        var repo = _unitOfWork.Repository<FAQ>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(FAQ), id);

        var oldValue = new { entity.CategoryId, entity.Question, entity.IsActive };

        entity.CategoryId = request.CategoryId;
        entity.Question = request.Question.Trim();
        entity.Answer = request.Answer.Trim();
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FAQ_UPDATED", nameof(FAQ), id, ipAddress, userAgent, oldValue, new { entity.CategoryId, entity.Question, entity.IsActive }, cancellationToken);

        return await MapFaqToDtoAsync(entity, cancellationToken);
    }

    public async Task DeleteFaqAsync(long id, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<FAQ>();
        var entity = await repo.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(FAQ), id);

        repo.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FAQ_DELETED", nameof(FAQ), id, ipAddress, userAgent, cancellationToken: cancellationToken);
    }

    public async Task UpdateFaqStatusAsync(long id, AdminUpdateFaqStatusRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<FAQ>();
        var entity = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(FAQ), id);

        var oldValue = new { entity.IsActive };
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedUser = adminUserId;

        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FAQ_STATUS_CHANGED", nameof(FAQ), id, ipAddress, userAgent, oldValue, new { entity.IsActive }, cancellationToken);
    }

    public async Task ReorderFaqsAsync(AdminReorderFaqsRequest request, long? adminUserId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var ids = request.Items.Select(x => x.Id).ToList();
        if (ids.Count != ids.Distinct().Count())
        {
            throw new AppValidationException(nameof(request.Items), "Ayni SSS Id'si listede birden fazla kez yer alamaz.");
        }

        var repo = _unitOfWork.Repository<FAQ>();
        var entities = await repo.GetQueryable(asNoTracking: false).Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);

        if (entities.Count != ids.Count)
        {
            throw new AppValidationException(nameof(request.Items), "Listede bulunmayan bir SSS Id'si var.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        foreach (var item in request.Items)
        {
            var entity = entities.First(x => x.Id == item.Id);
            entity.SortOrder = item.SortOrder;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedUser = adminUserId;
            repo.Update(entity);
        }

        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "FAQ_REORDERED", nameof(FAQ), null, ipAddress, userAgent,
            newValue: request.Items.Select(x => new { x.Id, x.SortOrder }), cancellationToken: cancellationToken);
    }

    private async Task ValidateCategoryAsync(long categoryId, CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.Repository<FAQCategory>().AnyAsync(x => x.Id == categoryId, cancellationToken);
        if (!exists)
        {
            throw new AppValidationException("categoryId", "Belirtilen kategori bulunamadi.");
        }
    }

    private async Task<FAQCategory> GetCategoryOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<FAQCategory>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(FAQCategory), id);
    }

    private async Task<FAQ> GetFaqOrThrowAsync(long id, CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<FAQ>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(FAQ), id);
    }

    private static AdminFaqCategoryDto MapCategoryToDto(FAQCategory entity, int faqCount) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        SortOrder = entity.SortOrder,
        FaqCount = faqCount,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };

    private async Task<AdminFaqDto> MapFaqToDtoAsync(FAQ entity, CancellationToken cancellationToken)
    {
        var categoryName = await _unitOfWork.Repository<FAQCategory>().GetQueryable()
            .Where(c => c.Id == entity.CategoryId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);

        return new AdminFaqDto
        {
            Id = entity.Id,
            CategoryId = entity.CategoryId,
            CategoryName = categoryName ?? "-",
            Question = entity.Question,
            Answer = entity.Answer,
            SortOrder = entity.SortOrder,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }
}
