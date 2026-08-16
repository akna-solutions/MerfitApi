using Merfit.Domain.Entities.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merfit.Infrastructure.Persistence.Configurations;

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ExternalCustomerId).HasMaxLength(128);
        builder.Property(s => s.ExternalSubscriptionId).HasMaxLength(128);
        builder.Property(s => s.ProductId).HasMaxLength(128).IsRequired();
        builder.Property(s => s.Plan).HasMaxLength(64).IsRequired();
        builder.Property(s => s.RawProviderData).HasColumnType("jsonb");

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.ExternalSubscriptionId);
    }
}

internal sealed class SubscriptionWebhookEventConfiguration : IEntityTypeConfiguration<SubscriptionWebhookEvent>
{
    public void Configure(EntityTypeBuilder<SubscriptionWebhookEvent> builder)
    {
        builder.ToTable("SubscriptionWebhookEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ExternalEventId).HasMaxLength(128).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.ProcessingError).HasMaxLength(2048);

        builder.HasIndex(e => e.ExternalEventId).IsUnique();
    }
}
