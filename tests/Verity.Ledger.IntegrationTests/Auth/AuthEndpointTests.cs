using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Verity.Ledger.Api.Controllers;
using Verity.Ledger.Application.Abstractions;
using Verity.Ledger.Domain.Users;
using Verity.Ledger.Infrastructure.Persistence;
using Verity.Ledger.IntegrationTests.Infrastructure;

namespace Verity.Ledger.IntegrationTests.Auth;

/// <summary>
/// Testes de ponta a ponta do login real (usuário persistido + BCrypt) contra uma Api real
/// (WebApplicationFactory) e um PostgreSQL real (Testcontainers) — ver ADR-007 e
/// docs/security.md.
/// </summary>
[Collection(LedgerIntegrationCollection.Name)]
public sealed class AuthEndpointTests : IAsyncLifetime
{
    private const string Username = "itest-comerciante";
    private const string Password = "senha-forte-123";

    private readonly LedgerApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = User.Register(Username, passwordHasher.Hash(Password), "Comerciante de Teste");
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task POST_login_com_credenciais_validas_retorna_token_JWT()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(Username, Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.Username.Should().Be(Username);
        body.DisplayName.Should().Be("Comerciante de Teste");
    }

    [Fact]
    public async Task POST_login_com_senha_incorreta_retorna_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(Username, "senha-errada"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_login_com_usuario_inexistente_retorna_401_com_a_mesma_mensagem_generica()
    {
        var invalidPassword = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(Username, "senha-errada"));
        var unknownUser = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nao-existe", "qualquer"));

        invalidPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownUser.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // traceId é único por requisição por natureza; o que importa para não vazar quais
        // usuários existem é que título/detalhe sejam idênticos nos dois casos.
        var invalidPasswordBody = await invalidPassword.Content.ReadFromJsonAsync<ProblemDetails>();
        var unknownUserBody = await unknownUser.Content.ReadFromJsonAsync<ProblemDetails>();
        unknownUserBody!.Title.Should().Be(invalidPasswordBody!.Title);
        unknownUserBody.Detail.Should().Be(invalidPasswordBody.Detail);
    }

    [Fact]
    public async Task Token_emitido_pelo_login_e_aceito_no_endpoint_protegido_de_transacoes()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(Username, Password));
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        var response = await _client.GetAsync("/api/v1/transactions?date=2026-01-01");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_login_sem_usuario_ou_senha_retorna_400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("", ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
