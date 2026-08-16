using Merfit.Domain.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merfit.Infrastructure.Persistence.Configurations;

internal sealed class UserEventConfiguration : IEntityTypeConfiguration<UserEvent>
{
    public void Configure(EntityTypeBuilder<UserEvent> builder)
    {
        builder.ToTable("UserEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventName).HasMaxLength(128).IsRequired();
        builder.Property(e => e.PropertiesJson).HasColumnType("jsonb");

        builder.HasIndex(e => new { e.EventName, e.CreatedAt });
        builder.HasIndex(e => e.UserId);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.EntityName).HasMaxLength(128).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(64);
        builder.Property(a => a.OldValuesJson).HasColumnType("jsonb");
        builder.Property(a => a.NewValuesJson).HasColumnType("jsonb");
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.UserAgent).HasMaxLength(512);

        builder.HasIndex(a => new { a.UserId, a.CreatedAt });
        builder.HasIndex(a => new { a.EntityName, a.EntityId });
    }
}
