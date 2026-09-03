namespace Verity.Ledger.IntegrationTests.Infrastructure;

/// <summary>
/// Força a execução sequencial dos testes de integração do Ledger (cada um sobe seus próprios
/// containers via Testcontainers). Mesmo racional do equivalente em
/// Verity.DailyBalance.IntegrationTests: evita contenção de recursos Docker sob paralelismo e
/// uma race condition conhecida do WebApplicationFactory/HostFactoryResolver ao construir
/// múltiplas instâncias para o mesmo entry point concorrentemente.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LedgerIntegrationCollection
{
    public const string Name = "Ledger Integration Tests";
}
