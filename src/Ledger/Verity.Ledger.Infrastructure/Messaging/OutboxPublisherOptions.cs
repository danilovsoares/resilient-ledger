namespace Verity.Ledger.Infrastructure.Messaging;

public sealed class OutboxPublisherOptions
{
    public const string SectionName = "OutboxPublisher";

    /// <summary>Intervalo entre ciclos de varredura da tabela outbox_messages.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Quantidade máxima de mensagens não publicadas processadas por ciclo.</summary>
    public int BatchSize { get; set; } = 50;
}
