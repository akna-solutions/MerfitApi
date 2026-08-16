using Merfit.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merfit.Infrastructure.Persistence.Configurations;

internal sealed class ExpoPushTokenConfiguration : IEntityTypeConfiguration<ExpoPushToken>
{
    public void Configure(EntityTypeBuilder<ExpoPushToken> builder)
    {
        builder.ToTable("ExpoPushTokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Token).HasMaxLength(256).IsRequired();
        builder.Property(t => t.DeviceId).HasMaxLength(128);
        builder.Property(t => t.DeviceName).HasMaxLength(128);
        builder.Property(t => t.AppVersion).HasMaxLength(32);
        builder.Property(t => t.Locale).HasMaxLength(16);
        builder.Property(t => t.Timezone).HasMaxLength(64);

        builder.HasIndex(t => t.Token).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.IsActive });
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Title).HasMaxLength(128).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(1024).IsRequired();
        builder.Property(n => n.DataJson).HasColumnType("jsonb");

        builder.HasIndex(n => new { n.UserId, n.CreatedAt });
        builder.HasIndex(n => new { n.UserId, n.ReadAt });
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences");
        builder.HasKey(p => p.UserId);
    }
}
