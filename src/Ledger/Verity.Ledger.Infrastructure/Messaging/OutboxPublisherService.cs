using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Verity.Ledger.Infrastructure.Persistence;

namespace Verity.Ledger.Infrastructure.Messaging;

/// <summary>
/// Publica de forma assíncrona as mensagens pendentes da Outbox no barramento (RabbitMQ via
/// MassTransit). Roda em ciclo de polling; cada mensagem só é marcada como publicada
/// (<c>published_at</c>) após confirmação do <see cref="IPublishEndpoint"/>. Se o broker estiver
/// indisponível, a mensagem permanece pendente e é reprocessada no próximo ciclo — o Ledger
/// continua aceitando novos lançamentos normalmente enquanto isso (ADR-002).
/// </summary>
public sealed class OutboxPublisherService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxPublisherOptions> options,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    private readonly OutboxPublisherOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha inesperada no ciclo do Outbox Publisher");
            }

            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Encerramento normal do host.
            }
        }
    }

    private async Task PublishPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var pending = await dbContext.OutboxMessages
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var message in pending)
        {
            try
            {
                var eventType = OutboxEventTypeRegistry.Resolve(message.Type);
                var payload = JsonSerializer.Deserialize(message.Payload, eventType)
                    ?? throw new InvalidOperationException("Payload de outbox vazio ou inválido.");

                await publishEndpoint.Publish(payload, eventType, context =>
                {
                    // MessageId é a identidade da mensagem no nível de transporte (MassTransit),
                    // distinta do EventId de negócio dentro do payload — mas, para rastreabilidade
                    // ponta a ponta, fixamos os dois com o mesmo valor em vez de deixar o
                    // MassTransit gerar um MessageId aleatório (ver docs/observability.md).
                    context.MessageId = message.Id;
                    context.CorrelationId = message.CorrelationId;
                    context.Headers.Set("CausationId", message.CausationId.ToString());
                }, cancellationToken);

                message.PublishedAt = DateTimeOffset.UtcNow;
                message.LastError = null;

                logger.LogInformation(
                    "Mensagem de outbox {OutboxMessageId} do tipo {EventType} publicada (CorrelationId {CorrelationId})",
                    message.Id, message.Type, message.CorrelationId);
            }
            catch (Exception ex)
            {
                message.RetryCount += 1;
                message.LastError = ex.Message;

                logger.LogWarning(ex,
                    "Falha ao publicar mensagem de outbox {OutboxMessageId} (tentativa {RetryCount}). Será reprocessada no próximo ciclo.",
                    message.Id, message.RetryCount);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
