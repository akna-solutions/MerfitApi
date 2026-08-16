using Merfit.Domain.Entities.Identity;
using Merfit.Domain.Entities.Profile;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merfit.Infrastructure.Persistence.Configurations;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("UserProfiles");
        builder.HasKey(p => p.UserId);

        builder.Property(p => p.HeightCm).HasPrecision(6, 2);
        builder.Property(p => p.WeightKg).HasPrecision(6, 2);
        builder.Property(p => p.StartingWeightKg).HasPrecision(6, 2);
        builder.Property(p => p.TargetWeightKg).HasPrecision(6, 2);

        builder.HasOne<User>().WithOne().HasForeignKey<UserProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Equipment).WithOne().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserEquipmentConfiguration : IEntityTypeConfiguration<UserEquipment>
{
    public void Configure(EntityTypeBuilder<UserEquipment> builder)
    {
        builder.ToTable("UserEquipment");
        builder.HasKey(e => new { e.UserId, e.Equipment });
    }
}

internal sealed class WeightEntryConfiguration : IEntityTypeConfiguration<WeightEntry>
{
    public void Configure(EntityTypeBuilder<WeightEntry> builder)
    {
        builder.ToTable("WeightEntries");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.WeightKg).HasPrecision(6, 2);
        builder.Property(w => w.Note).HasMaxLength(512);

        builder.HasIndex(w => new { w.UserId, w.RecordedAt });

        builder.HasOne<User>().WithMany().HasForeignKey(w => w.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("UserSettings");
        builder.HasKey(s => s.UserId);

        builder.Property(s => s.Language).HasMaxLength(8);
        builder.Property(s => s.Timezone).HasMaxLength(64);

        builder.HasOne<User>().WithOne().HasForeignKey<UserSettings>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
