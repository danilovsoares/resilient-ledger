using Verity.Ledger.Domain.Users;

namespace Verity.Ledger.Application.Abstractions;

public interface IUserRepository
{
    void Add(User user);

    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken);

    Task<bool> AnyAsync(CancellationToken cancellationToken);
}
