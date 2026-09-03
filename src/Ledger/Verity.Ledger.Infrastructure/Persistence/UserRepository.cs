using Microsoft.EntityFrameworkCore;
using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Domain.Users;

namespace Verity.Ledger.Infrastructure.Persistence;

public sealed class UserRepository(LedgerDbContext dbContext) : IUserRepository
{
    public void Add(User user) => dbContext.Users.Add(user);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken) =>
        dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username.Trim().ToLower(), cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking().AnyAsync(cancellationToken);
}
