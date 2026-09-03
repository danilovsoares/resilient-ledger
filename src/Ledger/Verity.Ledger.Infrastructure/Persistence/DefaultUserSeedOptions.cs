namespace Verity.Ledger.Infrastructure.Persistence;

/// <summary>
/// Credencial do comerciante usada para provisionar o primeiro (e, no escopo atual, único)
/// usuário do sistema — não há tela de cadastro (ver docs/security.md). Os valores vêm de
/// configuração (variável de ambiente localmente, Key Vault em produção), nunca do repositório.
/// </summary>
public sealed class DefaultUserSeedOptions
{
    public const string SectionName = "Auth:DefaultUser";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
