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
/// indisponível, a mensagem permanece pendente e é reprocessada no próximo ciclo, sem limite de
/// tentativas — o Ledger continua aceitando novos lançamentos normalmente enquanto isso
/// (ADR-002). Já uma falha permanente (tipo de evento desconhecido ou payload corrompido) marca
/// a mensagem como dead-lettered (<c>dead_lettered_at</c>) na primeira ocorrência, para não
/// reprocessar para sempre algo que retry nunca vai resolver.
/// </summary>
public sealed class OutboxPublisherService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxPublisherOptions> options,
    IOutboxEventTypeRegistry eventTypeRegistry,
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
            .Where(m => m.PublishedAt == null && m.DeadLetteredAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var message in pending)
        {
            Type eventType;
            object payload;
            try
            {
                eventType = eventTypeRegistry.Resolve(message.Type);
                payload = JsonSerializer.Deserialize(message.Payload, eventType)
                    ?? throw new InvalidOperationException("Payload de outbox vazio ou inválido.");
            }
            catch (Exception ex) when (IsPermanentFailure(ex))
            {
                // Marca como dead-lettered para não reprocessar para sempre (ver IsPermanentFailure);
                // as demais mensagens do lote seguem normalmente.
                message.RetryCount += 1;
                message.LastError = ex.Message;
                message.DeadLetteredAt = DateTimeOffset.UtcNow;

                logger.LogError(ex,
                    "Mensagem de outbox {OutboxMessageId} tem falha permanente (tipo de evento ou payload inválido) e não será mais reprocessada automaticamente. Requer investigação manual.",
                    message.Id);
                continue;
            }

            try
            {
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
                // Falha transitória (ex.: broker indisponível): sem limite de tentativas — a
                // mensagem continua pendente e será retentada no próximo ciclo (ADR-002/ADR-003).
                message.RetryCount += 1;
                message.LastError = ex.Message;

                logger.LogWarning(ex,
                    "Falha ao publicar mensagem de outbox {OutboxMessageId} (tentativa {RetryCount}). Será reprocessada no próximo ciclo.",
                    message.Id, message.RetryCount);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Falha permanente: nem o tipo do evento nem o payload gravado vão mudar sozinhos entre
    /// ciclos, então retentar nunca vai resolver. Qualquer outra exceção (tipicamente
    /// conectividade com o broker) é tratada como transitória, sem limite de tentativas.
    /// </summary>
    private static bool IsPermanentFailure(Exception ex) => ex is InvalidOperationException or JsonException;
}
