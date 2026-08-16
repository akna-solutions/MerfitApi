using Merfit.Domain.Entities.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merfit.Infrastructure.Persistence.Configurations;

internal sealed class ScoreRuleConfiguration : IEntityTypeConfiguration<ScoreRule>
{
    public void Configure(EntityTypeBuilder<ScoreRule> builder)
    {
        builder.ToTable("ScoreRules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Code).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(256).IsRequired();

        builder.HasIndex(r => r.Code).IsUnique();
    }
}

internal sealed class ScoreTransactionConfiguration : IEntityTypeConfiguration<ScoreTransaction>
{
    public void Configure(EntityTypeBuilder<ScoreTransaction> builder)
    {
        builder.ToTable("ScoreTransactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Type).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(256);

        builder.HasIndex(t => new { t.UserId, t.CreatedAt });
        builder.HasIndex(t => new { t.UserId, t.Type, t.ReferenceId });
    }
}

internal sealed class UserScoreConfiguration : IEntityTypeConfiguration<UserScore>
{
    public void Configure(EntityTypeBuilder<UserScore> builder)
    {
        builder.ToTable("UserScores");
        builder.HasKey(s => s.UserId);
        builder.HasIndex(s => s.TotalPoints);
    }
}
