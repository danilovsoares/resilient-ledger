using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Verity.Ledger.Api.Auth;

/// <summary>
/// Emissão de JWT compartilhada entre o login real (<see cref="Controllers.AuthController"/>) e
/// o emissor de desenvolvimento (<see cref="DevTokenEndpoint"/>) — mesma assinatura, mesmo
/// formato de claims, para que ambos produzam um token indistinguível para o middleware de
/// validação (<see cref="JwtAuthenticationExtensions"/>).
/// </summary>
public static class JwtTokenFactory
{
    public static string Create(JwtOptions jwtOptions, string subject, string name)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(ClaimTypes.Name, name),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtOptions.Issuer,
            audience: jwtOptions.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(jwtOptions.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
