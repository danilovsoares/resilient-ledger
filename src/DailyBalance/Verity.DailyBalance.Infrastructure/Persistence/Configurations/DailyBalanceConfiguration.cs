using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DailyBalanceAggregate = Verity.DailyBalance.Domain.DailyBalances.DailyBalance;

namespace Verity.DailyBalance.Infrastructure.Persistence.Configurations;

public sealed class DailyBalanceConfiguration : IEntityTypeConfiguration<DailyBalanceAggregate>
{
    public void Configure(EntityTypeBuilder<DailyBalanceAggregate> builder)
    {
        builder.ToTable("daily_balances");

        builder.HasKey(b => b.BusinessDate);

        builder.Property(b => b.BusinessDate).ValueGeneratedNever();

        builder.Property(b => b.TotalCredits).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(b => b.TotalDebits).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(b => b.UpdatedAt).IsRequired();

        builder.Ignore(b => b.Balance);
    }
}
