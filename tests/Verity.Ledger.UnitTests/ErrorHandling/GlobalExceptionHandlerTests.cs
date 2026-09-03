using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Verity.Ledger.Api.ErrorHandling;
using Verity.Ledger.Domain.Exceptions;

namespace Verity.Ledger.UnitTests.ErrorHandling;

public sealed class GlobalExceptionHandlerTests
{
    private readonly GlobalExceptionHandler _handler = new(NullLogger<GlobalExceptionHandler>.Instance);

    [Fact]
    public async Task DomainException_gera_400_com_a_mensagem_de_negocio()
    {
        var context = CreateHttpContext();

        var handled = await _handler.TryHandleAsync(context, new DomainException("valor inválido"), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var problem = await ReadProblemDetailsAsync(context);
        problem!.Detail.Should().Be("valor inválido", "erros de domínio podem expor a mensagem de negócio ao cliente");
    }

    [Fact]
    public async Task Excecao_generica_gera_500_sem_expor_detalhes_internos()
    {
        var context = CreateHttpContext();
        var internalException = new InvalidOperationException("connection string leaked: Host=prod-db;Password=segredo");

        var handled = await _handler.TryHandleAsync(context, internalException, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        var problem = await ReadProblemDetailsAsync(context);
        problem!.Detail.Should().NotContain("segredo").And.NotContain("connection string",
            "detalhes de infraestrutura nunca devem vazar na resposta ao cliente (ver docs/security.md)");
    }

    [Fact]
    public async Task Cliente_desconectado_e_tratado_sem_gerar_resposta_de_erro()
    {
        var context = CreateHttpContext();
        using var cts = new CancellationTokenSource();
        context.RequestAborted = cts.Token;
        cts.Cancel();

        var handled = await _handler.TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        handled.Should().BeTrue();
        // Nada é escrito no corpo da resposta — não há Problem Details para um cliente que já foi embora.
        context.Response.Body.Length.Should().Be(0);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/transactions";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<ProblemDetails?> ReadProblemDetailsAsync(DefaultHttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body);
    }
}
