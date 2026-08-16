using Merfit.Domain.Entities.WorkoutSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merfit.Infrastructure.Persistence.Configurations;

internal sealed class WorkoutSessionConfiguration : IEntityTypeConfiguration<WorkoutSession>
{
    public void Configure(EntityTypeBuilder<WorkoutSession> builder)
    {
        builder.ToTable("WorkoutSessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TotalVolume).HasPrecision(10, 2);

        builder.HasIndex(s => new { s.UserId, s.StartedAt });
        builder.HasIndex(s => new { s.UserId, s.Status });

        builder.HasMany(s => s.ExerciseLogs).WithOne().HasForeignKey(l => l.WorkoutSessionId).OnDelete(DeleteBehavior.Cascade);

        // Optimistic concurrency: two devices completing/patching the same session concurrently
        // (e.g. resume-and-save races) should not silently clobber each other. Postgres has no
        // native rowversion type, so this maps to the system xmin column instead.
        builder.UseXminAsConcurrencyToken();
    }
}

internal sealed class WorkoutExerciseLogConfiguration : IEntityTypeConfiguration<WorkoutExerciseLog>
{
    public void Configure(EntityTypeBuilder<WorkoutExerciseLog> builder)
    {
        builder.ToTable("WorkoutExerciseLogs");
        builder.HasKey(l => l.Id);

        builder.HasIndex(l => l.WorkoutSessionId);

        builder.HasMany(l => l.Sets).WithOne().HasForeignKey(s => s.WorkoutExerciseLogId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SetLogConfiguration : IEntityTypeConfiguration<SetLog>
{
    public void Configure(EntityTypeBuilder<SetLog> builder)
    {
        builder.ToTable("SetLogs");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.WeightKg).HasPrecision(6, 2);

        builder.HasIndex(s => s.WorkoutExerciseLogId);
    }
}

internal sealed class PersonalRecordConfiguration : IEntityTypeConfiguration<PersonalRecord>
{
    public void Configure(EntityTypeBuilder<PersonalRecord> builder)
    {
        builder.ToTable("PersonalRecords");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.WeightKg).HasPrecision(6, 2);
        builder.Property(p => p.Estimated1RM).HasPrecision(6, 2);

        builder.HasIndex(p => new { p.UserId, p.ExerciseId, p.RecordType });
    }
}

internal sealed class UserWorkoutStreakConfiguration : IEntityTypeConfiguration<UserWorkoutStreak>
{
    public void Configure(EntityTypeBuilder<UserWorkoutStreak> builder)
    {
        builder.ToTable("UserWorkoutStreaks");
        builder.HasKey(s => s.UserId);
    }
}
