using Microsoft.EntityFrameworkCore;
using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Domain.Users;

namespace Verity.Ledger.Infrastructure.Persistence;

/// <summary>
/// Provisiona o usuário comerciante padrão se a tabela de usuários estiver vazia. Só deve
/// rodar quando explicitamente habilitado (<c>Auth:SeedDefaultUser=true</c>) — nunca por padrão
/// em produção, para não criar uma credencial previsível automaticamente.
/// </summary>
public static class DefaultUserSeeder
{
    public static async Task SeedAsync(
        LedgerDbContext dbContext,
        IPasswordHasher passwordHasher,
        DefaultUserSeedOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
        {
            return;
        }

        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var user = User.Register(options.Username, passwordHasher.Hash(options.Password), options.DisplayName);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
