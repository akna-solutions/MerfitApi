using Merfit.Domain.Entities.Progress;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merfit.Infrastructure.Persistence.Configurations;

internal sealed class BodyMetricEntryConfiguration : IEntityTypeConfiguration<BodyMetricEntry>
{
    public void Configure(EntityTypeBuilder<BodyMetricEntry> builder)
    {
        builder.ToTable("BodyMetricEntries");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Value).HasPrecision(7, 2);
        builder.Property(m => m.Unit).HasMaxLength(16).IsRequired();

        builder.HasIndex(m => new { m.UserId, m.MetricType, m.RecordedAt });
    }
}
