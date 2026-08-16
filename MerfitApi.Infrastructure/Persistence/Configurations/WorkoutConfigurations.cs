using Merfit.Domain.Entities.Workouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merfit.Infrastructure.Persistence.Configurations;

internal sealed class WorkoutCategoryConfiguration : IEntityTypeConfiguration<WorkoutCategory>
{
    public void Configure(EntityTypeBuilder<WorkoutCategory> builder)
    {
        builder.ToTable("WorkoutCategories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(64).IsRequired();
        builder.HasIndex(c => c.Slug).IsUnique();
    }
}

internal sealed class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> builder)
    {
        builder.ToTable("Exercises");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(128).IsRequired();
        builder.Property(e => e.ImageUrl).HasMaxLength(1024);
        builder.Property(e => e.VideoUrl).HasMaxLength(1024);

        builder.HasIndex(e => e.Name);
        builder.HasIndex(e => e.MuscleGroup);
    }
}

internal sealed class WorkoutConfiguration : IEntityTypeConfiguration<Workout>
{
    public void Configure(EntityTypeBuilder<Workout> builder)
    {
        builder.ToTable("Workouts");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Title).HasMaxLength(128).IsRequired();
        builder.Property(w => w.Tagline).HasMaxLength(256);
        builder.Property(w => w.ImageUrl).HasMaxLength(1024);

        builder.HasIndex(w => w.Title);
        builder.HasIndex(w => new { w.Difficulty, w.MuscleGroup, w.IsActive });
        builder.HasIndex(w => w.Featured);
        builder.HasIndex(w => w.CategoryId);

        builder.HasOne(w => w.Category).WithMany().HasForeignKey(w => w.CategoryId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(w => w.Equipment).WithOne().HasForeignKey(e => e.WorkoutId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(w => w.Exercises).WithOne().HasForeignKey(e => e.WorkoutId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WorkoutEquipmentConfiguration : IEntityTypeConfiguration<WorkoutEquipment>
{
    public void Configure(EntityTypeBuilder<WorkoutEquipment> builder)
    {
        builder.ToTable("WorkoutEquipment");
        builder.HasKey(e => new { e.WorkoutId, e.Equipment });
    }
}

internal sealed class WorkoutExerciseConfiguration : IEntityTypeConfiguration<WorkoutExercise>
{
    public void Configure(EntityTypeBuilder<WorkoutExercise> builder)
    {
        builder.ToTable("WorkoutExercises");
        builder.HasKey(we => we.Id);
        builder.Property(we => we.Notes).HasMaxLength(512);

        builder.HasIndex(we => new { we.WorkoutId, we.SortOrder });

        builder.HasOne(we => we.Exercise).WithMany().HasForeignKey(we => we.ExerciseId).OnDelete(DeleteBehavior.Restrict);
    }
}
