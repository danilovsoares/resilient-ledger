using Verity.Ledger.Domain.Abstractions;
using Verity.Ledger.Domain.Exceptions;

namespace Verity.Ledger.Domain.Users;

/// <summary>
/// Usuário autorizado a operar o fluxo de caixa. O domínio de negócio deste desafio é um único
/// comerciante (ver docs/architecture/01-contexto-e-objetivos.md), então não há papéis nem
/// múltiplos perfis — apenas identidade e credencial. O hash da senha é opaco para o domínio;
/// quem gera e verifica é a infraestrutura (ver <c>IPasswordHasher</c>).
/// </summary>
public sealed class User : AggregateRoot
{
    public Guid Id { get; private set; }
    public string Username { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private User() { }

    public static User Register(string username, string passwordHash, string displayName)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new DomainException("O nome de usuário é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("O hash de senha é obrigatório.");
        }

        return new User
        {
            Id = Guid.NewGuid(),
            Username = username.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
