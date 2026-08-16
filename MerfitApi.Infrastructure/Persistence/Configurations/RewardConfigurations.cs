using Merfit.Domain.Entities.Rewards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merfit.Infrastructure.Persistence.Configurations;

internal sealed class RewardConfiguration : IEntityTypeConfiguration<Reward>
{
    public void Configure(EntityTypeBuilder<Reward> builder)
    {
        builder.ToTable("Rewards");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Title).HasMaxLength(128).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(512).IsRequired();
        builder.Property(r => r.ImageUrl).HasMaxLength(1024);
        builder.Property(r => r.Value).HasMaxLength(128).IsRequired();

        builder.HasIndex(r => new { r.Period, r.Rank });
    }
}

internal sealed class RewardRedemptionConfiguration : IEntityTypeConfiguration<RewardRedemption>
{
    public void Configure(EntityTypeBuilder<RewardRedemption> builder)
    {
        builder.ToTable("RewardRedemptions");
        builder.HasKey(r => r.Id);

        builder.HasIndex(r => new { r.UserId, r.Period, r.PeriodRank });

        builder.HasOne<Reward>().WithMany().HasForeignKey(r => r.RewardId).OnDelete(DeleteBehavior.Restrict);
    }
}
