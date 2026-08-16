using Merfit.Domain.Entities.Nutrition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merfit.Infrastructure.Persistence.Configurations;

internal sealed class FoodConfiguration : IEntityTypeConfiguration<Food>
{
    public void Configure(EntityTypeBuilder<Food> builder)
    {
        builder.ToTable("Foods");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Name).HasMaxLength(256).IsRequired();
        builder.Property(f => f.Brand).HasMaxLength(128);
        builder.Property(f => f.ServingUnit).HasMaxLength(32);
        builder.Property(f => f.Barcode).HasMaxLength(64);
        builder.Property(f => f.CaloriesPer100g).HasPrecision(7, 2);
        builder.Property(f => f.ProteinPer100g).HasPrecision(7, 2);
        builder.Property(f => f.CarbsPer100g).HasPrecision(7, 2);
        builder.Property(f => f.FatPer100g).HasPrecision(7, 2);
        builder.Property(f => f.ServingSize).HasPrecision(7, 2);

        builder.HasIndex(f => f.Name);
        builder.HasIndex(f => f.Barcode);
    }
}

internal sealed class DailyNutritionConfiguration : IEntityTypeConfiguration<DailyNutrition>
{
    public void Configure(EntityTypeBuilder<DailyNutrition> builder)
    {
        builder.ToTable("DailyNutritions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.WaterTargetLiters).HasPrecision(4, 2);
        builder.Property(d => d.WaterConsumedLiters).HasPrecision(4, 2);

        builder.HasIndex(d => new { d.UserId, d.Date }).IsUnique();
    }
}

internal sealed class MealConfiguration : IEntityTypeConfiguration<Meal>
{
    public void Configure(EntityTypeBuilder<Meal> builder)
    {
        builder.ToTable("Meals");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).HasMaxLength(256).IsRequired();
        builder.Property(m => m.Notes).HasMaxLength(512);
        builder.Property(m => m.TotalCalories).HasPrecision(7, 2);

        builder.HasIndex(m => new { m.UserId, m.Date });

        builder.HasMany(m => m.Items).WithOne().HasForeignKey(i => i.MealId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MealItemConfiguration : IEntityTypeConfiguration<MealItem>
{
    public void Configure(EntityTypeBuilder<MealItem> builder)
    {
        builder.ToTable("MealItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Name).HasMaxLength(256).IsRequired();
        builder.Property(i => i.Unit).HasMaxLength(32).IsRequired();
        builder.Property(i => i.Quantity).HasPrecision(7, 2);
        builder.Property(i => i.Calories).HasPrecision(7, 2);
        builder.Property(i => i.Protein).HasPrecision(7, 2);
        builder.Property(i => i.Carbs).HasPrecision(7, 2);
        builder.Property(i => i.Fat).HasPrecision(7, 2);

        builder.HasIndex(i => i.MealId);

        builder.HasOne<Food>().WithMany().HasForeignKey(i => i.FoodId).OnDelete(DeleteBehavior.SetNull);
    }
}
