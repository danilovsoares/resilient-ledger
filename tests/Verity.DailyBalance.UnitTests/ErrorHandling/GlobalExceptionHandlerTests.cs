using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Verity.DailyBalance.Api.ErrorHandling;
using Verity.DailyBalance.Domain.Exceptions;

namespace Verity.DailyBalance.UnitTests.ErrorHandling;

public sealed class GlobalExceptionHandlerTests
{
    private readonly GlobalExceptionHandler _handler = new(NullLogger<GlobalExceptionHandler>.Instance);

    [Fact]
    public async Task DomainException_gera_400_com_a_mensagem_de_negocio()
    {
        var context = CreateHttpContext();

        var handled = await _handler.TryHandleAsync(context, new DomainException("data inválida"), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var problem = await ReadProblemDetailsAsync(context);
        problem!.Detail.Should().Be("data inválida");
    }

    [Fact]
    public async Task Excecao_generica_gera_500_sem_expor_detalhes_internos()
    {
        var context = CreateHttpContext();
        var internalException = new InvalidOperationException("Redis connection string: redis://prod:segredo@host");

        var handled = await _handler.TryHandleAsync(context, internalException, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        var problem = await ReadProblemDetailsAsync(context);
        problem!.Detail.Should().NotContain("segredo").And.NotContain("Redis connection string",
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
        context.Response.Body.Length.Should().Be(0);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/v1/daily-balances/2026-09-02";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<ProblemDetails?> ReadProblemDetailsAsync(DefaultHttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body);
    }
}
