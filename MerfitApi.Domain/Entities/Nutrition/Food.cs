using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Nutrition;

public sealed class Food : AuditableEntity
{
    public required string Name { get; set; }
    public string? Brand { get; set; }
    public decimal CaloriesPer100g { get; set; }
    public decimal ProteinPer100g { get; set; }
    public decimal CarbsPer100g { get; set; }
    public decimal FatPer100g { get; set; }
    public decimal? ServingSize { get; set; }
    public string? ServingUnit { get; set; }
    public string? Barcode { get; set; }
    public bool IsVerified { get; set; }
    public FoodSource Source { get; set; } = FoodSource.System;
}
