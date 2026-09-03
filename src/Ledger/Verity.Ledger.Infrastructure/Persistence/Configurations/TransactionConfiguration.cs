using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Verity.Ledger.Domain.Transactions;

namespace Verity.Ledger.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Type)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(t => t.Amount)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(t => t.OccurredAt).IsRequired();
        builder.Property(t => t.BusinessDate).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500);

        builder.Property(t => t.IdempotencyKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(t => t.CreatedAt).IsRequired();

        builder.Property(t => t.ReversalOfTransactionId);

        builder.HasIndex(t => t.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ix_transactions_idempotency_key");

        builder.HasIndex(t => t.BusinessDate)
            .HasDatabaseName("ix_transactions_business_date");

        builder.HasIndex(t => t.ReversalOfTransactionId)
            .HasDatabaseName("ix_transactions_reversal_of_transaction_id");

        builder.Ignore(t => t.DomainEvents);
    }
}
