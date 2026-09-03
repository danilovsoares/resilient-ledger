using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Verity.Ledger.Infrastructure.Persistence;
using Verity.Ledger.IntegrationTests.Infrastructure;
using Verity.Shared.Contracts.IntegrationEvents;

namespace Verity.Ledger.IntegrationTests.Messaging;

/// <summary>
/// Prova as duas metades da correção do OutboxPublisherService: uma falha permanente (tipo de
/// evento desconhecido) para de ser retentada após a primeira tentativa, enquanto uma falha
/// transitória (broker indisponível — o padrão desta factory, ver LedgerApiFactory) nunca é
/// classificada como dead-letter — a garantia central do ADR-002/ADR-003 (nenhuma falha de
/// broker faz uma mensagem parar de ser retentada) não pode regredir junto com a correção do
/// problema de retry infinito.
/// </summary>
[Collection(LedgerIntegrationCollection.Name)]
public sealed class OutboxDeadLetterTests : IAsyncLifetime
{
    private readonly LedgerApiFactory _factory = new();

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Mensagem_com_tipo_de_evento_desconhecido_e_marcada_dead_lettered_e_nunca_mais_retentada()
    {
        var messageId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = messageId,
                Type = "Verity.Ledger.Tests.EventoTipoInexistente",
                Payload = "{}",
                CorrelationId = Guid.NewGuid(),
                CausationId = Guid.NewGuid(),
                OccurredAt = DateTimeOffset.UtcNow,
                RetryCount = 0,
            });
            await dbContext.SaveChangesAsync();
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        OutboxMessage? message = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
            message = await dbContext.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            if (message.DeadLetteredAt is not null)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        message.Should().NotBeNull();
        message!.DeadLetteredAt.Should().NotBeNull("um tipo de evento desconhecido nunca vai se resolver sozinho entre ciclos");
        message.PublishedAt.Should().BeNull();
        message.RetryCount.Should().Be(1, "a falha permanente é detectada e marcada já na primeira tentativa");

        // Espera mais alguns ciclos de polling (PollingInterval = 1s neste factory) para provar
        // que a mensagem realmente parou de ser selecionada — não só que ainda não foi retentada.
        await Task.Delay(TimeSpan.FromSeconds(3));

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
            var after = await dbContext.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            after.RetryCount.Should().Be(1, "uma falha permanente não deve ser reprocessada indefinidamente");
            after.PublishedAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task Falha_transitoria_de_broker_nunca_marca_a_mensagem_como_dead_lettered()
    {
        var messageId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
            var integrationEvent = new TransactionRegisteredEvent(
                EventId: Guid.NewGuid(),
                TransactionId: Guid.NewGuid(),
                Type: TransactionType.Credit,
                Amount: 10m,
                BusinessDate: DateOnly.FromDateTime(DateTime.UtcNow),
                OccurredAtUtc: DateTimeOffset.UtcNow,
                CorrelationId: Guid.NewGuid(),
                CausationId: Guid.NewGuid());

            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = messageId,
                Type = typeof(TransactionRegisteredEvent).FullName!,
                Payload = JsonSerializer.Serialize(integrationEvent),
                CorrelationId = Guid.NewGuid(),
                CausationId = Guid.NewGuid(),
                OccurredAt = DateTimeOffset.UtcNow,
                RetryCount = 0,
            });
            await dbContext.SaveChangesAsync();
        }

        // Este factory aponta para um RabbitMQ inalcançável por padrão — o cenário transitório
        // clássico do ADR-002. Nota: o IPublishEndpoint.Publish do MassTransit não lança rápido
        // contra um broker inalcançável — ele fica aguardando reconexão (comportamento do
        // próprio MassTransit, fora do escopo desta correção). Por isso este teste não verifica
        // incremento de RetryCount (que dependeria de quando o MassTransit desiste de
        // reconectar, não determinístico em segundos); verifica só a garantia que interessa
        // aqui: uma falha transitória nunca marca a mensagem como dead-lettered.
        await Task.Delay(TimeSpan.FromSeconds(5));

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
            var current = await dbContext.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);

            current.DeadLetteredAt.Should().BeNull("falha de broker é transitória, nunca deve marcar dead-letter");
            current.PublishedAt.Should().BeNull();
        }
    }
}
