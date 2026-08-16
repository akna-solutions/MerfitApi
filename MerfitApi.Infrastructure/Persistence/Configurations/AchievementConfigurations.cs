using Merfit.Domain.Entities.Achievements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merfit.Infrastructure.Persistence.Configurations;

internal sealed class AchievementConfiguration : IEntityTypeConfiguration<Achievement>
{
    public void Configure(EntityTypeBuilder<Achievement> builder)
    {
        builder.ToTable("Achievements");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Code).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Title).HasMaxLength(128).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(512).IsRequired();
        builder.Property(a => a.Icon).HasMaxLength(64).IsRequired();

        builder.HasIndex(a => a.Code).IsUnique();
    }
}

internal sealed class UserAchievementConfiguration : IEntityTypeConfiguration<UserAchievement>
{
    public void Configure(EntityTypeBuilder<UserAchievement> builder)
    {
        builder.ToTable("UserAchievements");
        builder.HasKey(ua => ua.Id);

        builder.HasIndex(ua => new { ua.UserId, ua.AchievementId }).IsUnique();

        builder.HasOne<Achievement>().WithMany().HasForeignKey(ua => ua.AchievementId).OnDelete(DeleteBehavior.Cascade);
    }
}
