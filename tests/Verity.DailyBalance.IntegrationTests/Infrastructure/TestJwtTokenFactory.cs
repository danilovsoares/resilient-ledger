using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Verity.DailyBalance.IntegrationTests.Infrastructure;

/// <summary>
/// Emite tokens de teste com a mesma chave/issuer/audience configurados em
/// <see cref="DailyBalanceApiFactory"/>, já que a Api do Daily Balance (diferente do Ledger)
/// não expõe um endpoint de emissão de token de desenvolvimento.
/// </summary>
public static class TestJwtTokenFactory
{
    // Mesma chave de appsettings.Development.json (ambos os serviços). WebApplicationFactory,
    // no modelo de minimal hosting, carrega appsettings.{Environment}.json com precedência
    // sobre o ConfigureAppConfiguration do teste — por isso usamos a própria chave de dev em
    // vez de tentar sobrepô-la.
    public const string SigningKey = "dev-only-signing-key-not-for-production-use-please-change-me-32bytes+";
    private const string Issuer = "verity-local";
    private const string Audience = "verity-clients";

    public static string CreateToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(ClaimTypes.Name, "integration-test-user")],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
