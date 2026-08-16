using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Nutrition;

public sealed class Meal : BaseEntity
{
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public MealType MealType { get; set; }
    public required string Name { get; set; }
    public decimal TotalCalories { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset LoggedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<MealItem> Items { get; set; } = [];
}

public sealed class MealItem : BaseEntity
{
    public Guid MealId { get; set; }
    public Guid? FoodId { get; set; }
    public required string Name { get; set; }
    public decimal Quantity { get; set; } = 1;
    public required string Unit { get; set; }
    public decimal Calories { get; set; }
    public decimal Protein { get; set; }
    public decimal Carbs { get; set; }
    public decimal Fat { get; set; }
}
