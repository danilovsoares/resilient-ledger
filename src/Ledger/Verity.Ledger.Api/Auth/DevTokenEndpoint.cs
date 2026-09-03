namespace Verity.Ledger.Api.Auth;

/// <summary>
/// Emissor de token de desenvolvimento, usado pelos testes de integração para exercitar
/// endpoints protegidos por JWT Bearer sem depender de uma credencial de usuário real. Nunca é
/// registrado fora de Development (ver Program.cs). Para o login de fato usado pela aplicação
/// Web, ver <see cref="Controllers.AuthController"/>.
/// </summary>
public static class DevTokenEndpoint
{
    public static void MapDevTokenEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/dev/token", (JwtOptions jwtOptions) =>
        {
            var token = JwtTokenFactory.Create(jwtOptions, subject: "dev-user", name: "merchant-dev-user");
            return Results.Ok(new { accessToken = token });
        })
        .WithTags("Dev")
        .ExcludeFromDescription();
    }
}
