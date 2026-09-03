using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Verity.DailyBalance.Infrastructure.Persistence.Configurations;

public sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages");

        builder.HasKey(p => p.EventId);
        builder.Property(p => p.EventId).ValueGeneratedNever();

        builder.Property(p => p.EventType).HasMaxLength(256).IsRequired();
        builder.Property(p => p.ProcessedAt).IsRequired();
        builder.Property(p => p.CorrelationId).IsRequired();

        // EventId é a própria chave primária: a restrição de unicidade exigida para a Inbox
        // é garantida pelo índice de chave primária, sem necessidade de um índice único adicional.
    }
}
