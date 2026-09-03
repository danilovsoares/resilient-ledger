namespace Verity.DailyBalance.IntegrationTests.Infrastructure;

/// <summary>
/// Força a execução sequencial dos testes de integração (cada um sobe seus próprios containers
/// Postgres/Redis via Testcontainers). Evita: (1) contenção de recursos Docker sob paralelismo,
/// que causa timeouts esporádicos de conexão; (2) uma race condition conhecida do
/// WebApplicationFactory/HostFactoryResolver ao construir múltiplas instâncias para o mesmo
/// entry point concorrentemente.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DailyBalanceIntegrationCollection
{
    public const string Name = "DailyBalance Integration Tests";
}
