using Microsoft.EntityFrameworkCore;
using DailyBalanceAggregate = Verity.DailyBalance.Domain.DailyBalances.DailyBalance;

namespace Verity.DailyBalance.Infrastructure.Persistence;

public sealed class DailyBalanceDbContext(DbContextOptions<DailyBalanceDbContext> options) : DbContext(options)
{
    public DbSet<DailyBalanceAggregate> DailyBalances => Set<DailyBalanceAggregate>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DailyBalanceDbContext).Assembly);
    }
}
