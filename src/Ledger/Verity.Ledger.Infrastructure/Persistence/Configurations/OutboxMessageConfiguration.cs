using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Verity.Ledger.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Type).HasMaxLength(256).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.CorrelationId).IsRequired();
        builder.Property(m => m.CausationId).IsRequired();
        builder.Property(m => m.OccurredAt).IsRequired();
        builder.Property(m => m.RetryCount).HasDefaultValue(0);
        builder.Property(m => m.LastError).HasMaxLength(2000);

        builder.HasIndex(m => m.PublishedAt)
            .HasDatabaseName("ix_outbox_messages_published_at");
    }
}
