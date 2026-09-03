namespace Verity.DailyBalance.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "verity-local";
    public string Audience { get; set; } = "verity-clients";

    /// <summary>
    /// Chave simétrica de assinatura. Em desenvolvimento, definida em appsettings.Development.json.
    /// Em produção, deve vir de variável de ambiente ou Azure Key Vault — nunca do repositório
    /// (ver docs/security.md). Deve ser a mesma chave configurada no Ledger, pois ambos os
    /// serviços validam tokens emitidos pelo mesmo emissor de desenvolvimento.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public int ExpirationMinutes { get; set; } = 60;
}
