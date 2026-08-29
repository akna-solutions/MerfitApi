using MerfitApi.Business.Common.Pagination;
using MerfitApi.Business.Dtos.Admin.Nutrition;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminNutritionService'in varsayilan implementasyonu. Tamamen salt-okunurdur.</summary>
public class AdminNutritionService : IAdminNutritionService
{
    private readonly IUnitOfWork _unitOfWork;

    public AdminNutritionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<AdminMealListItemDto>> GetMealsAsync(AdminMealListRequest request, CancellationToken cancellationToken = default)
    {
        var meals = _unitOfWork.Repository<Meal>().GetQueryable();
        var users = _unitOfWork.Repository<ApplicationUser>().GetQueryable();
        var mealItems = _unitOfWork.Repository<MealItem>().GetQueryable();

        var query = meals.AsQueryable();

        if (request.UserId.HasValue) query = query.Where(m => m.UserId == request.UserId.Value);
        if (request.From.HasValue) query = query.Where(m => m.Date >= request.From.Value);
        if (request.To.HasValue) query = query.Where(m => m.Date <= request.To.Value);

        query = query.OrderByDescending(m => m.Date);

        var totalCount = await query.LongCountAsync(cancellationToken);

        var page = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m => new AdminMealListItemDto
            {
                Id = m.Id,
                UserId = m.UserId,
                UserEmail = users.Where(u => u.Id == m.UserId).Select(u => u.Email).FirstOrDefault() ?? "-",
                Date = m.Date,
                Notes = m.Notes,
                ItemCount = mealItems.Count(mi => mi.MealId == m.Id),
                TotalCalories = mealItems.Where(mi => mi.MealId == m.Id).Sum(mi => (decimal?)mi.Calories) ?? 0m,
            })
            .ToListAsync(cancellationToken);

        return PagedResult<AdminMealListItemDto>.Create(page, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminMealDetailDto> GetMealByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var meal = await _unitOfWork.Repository<Meal>().GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Meal), id);

        var userEmail = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.Id == meal.UserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        var items = _unitOfWork.Repository<MealItem>().GetQueryable();
        var foods = _unitOfWork.Repository<Food>().GetQueryable();

        var itemDtos = await (from mi in items
                               where mi.MealId == id
                               join f in foods on mi.FoodId equals f.Id
                               select new AdminMealItemDetailDto
                               {
                                   Id = mi.Id,
                                   FoodId = mi.FoodId,
                                   FoodName = f.Name,
                                   Quantity = mi.Quantity,
                                   ServingSize = mi.ServingSize,
                                   Calories = mi.Calories,
                                   Protein = mi.Protein,
                                   Carbs = mi.Carbs,
                                   Fat = mi.Fat,
                                   Fiber = mi.Fiber,
                               }).ToListAsync(cancellationToken);

        return new AdminMealDetailDto
        {
            Id = meal.Id,
            UserId = meal.UserId,
            UserEmail = userEmail ?? "-",
            Date = meal.Date,
            Notes = meal.Notes,
            ItemCount = itemDtos.Count,
            TotalCalories = itemDtos.Sum(x => x.Calories),
            Items = itemDtos,
        };
    }
}
