# 04 — Componentes e Camadas

## Objetivo

Explicar a estrutura interna de cada backend, organizada em Clean/Hexagonal Architecture, e
como CQRS é aplicado de forma pragmática — sem rótulos vazios.

## Escopo

Estrutura interna dos projetos `.NET` em `src/Ledger/` e `src/DailyBalance/`. Para os fluxos
que atravessam essas camadas, ver [05-fluxos-principais.md](05-fluxos-principais.md).

## Estrutura de projetos

Cada serviço é dividido em quatro projetos .NET, com dependências apontando sempre para
dentro (Api/Infrastructure → Application → Domain), nunca o contrário:

```text
Verity.Ledger.Domain          (sem dependências de framework)
Verity.Ledger.Application     → Domain
Verity.Ledger.Infrastructure  → Application, Domain
Verity.Ledger.Api             → Application, Infrastructure

Verity.DailyBalance.Domain
Verity.DailyBalance.Application     → Domain
Verity.DailyBalance.Infrastructure  → Application, Domain
Verity.DailyBalance.Api             → Application, Infrastructure
Verity.DailyBalance.Worker          → Application, Infrastructure
```

Um projeto `Verity.Shared.Contracts` (em `src/Shared/`) contém apenas o contrato de
integração publicado no broker (`TransactionRegisteredEvent`, o enum de tipo de lançamento
usado no evento e as constantes de cabeçalho de correlação). Ele é referenciado pelas camadas
de Application/Infrastructure/Api dos dois serviços, mas **não** pelas camadas de Domain — o
domínio de cada serviço define seu próprio enum de tipo de lançamento
(`Verity.Ledger.Domain.Transactions.TransactionType` e
`Verity.DailyBalance.Domain.DailyBalances.TransactionKind`), desacoplado do formato do evento
publicado no broker. A tradução entre o enum de domínio e o enum de contrato acontece na
camada de Application (`RegisterTransactionHandler` no Ledger,
`TransactionRegisteredConsumer` no Daily Balance).

## Camadas

### `Domain`

Entidades, value objects implícitos, regras de negócio e eventos de domínio. Sem
dependência de ASP.NET Core, EF Core ou qualquer biblioteca de infraestrutura.

- **Ledger**: `Transaction` (agregado raiz) — cria-se via `Transaction.Register(...)`, que
  valida que o valor é positivo e que a `Idempotency-Key` foi informada, e levanta o evento
  de domínio `TransactionRegisteredDomainEvent`.
- **Daily Balance**: `DailyBalance` (agregado raiz da projeção) — `DailyBalance.Apply(kind,
  amount)` incrementa `TotalCredits`/`TotalDebits`; `Balance` é uma propriedade calculada
  (`TotalCredits - TotalDebits`), nunca persistida diretamente.

### `Application`

Casos de uso (comandos e queries), DTOs, validação e orquestração. Não conhece EF Core,
RabbitMQ ou Redis diretamente — depende apenas de abstrações (`ITransactionRepository`,
`IOutboxWriter`, `IUnitOfWork`, `IDailyBalanceCache` etc.) implementadas na camada de
Infrastructure.

- **Ledger**: `RegisterTransactionCommand`/`RegisterTransactionHandler` (com validação via
  FluentValidation em `RegisterTransactionValidator`) e
  `GetTransactionsByDateQuery`/`GetTransactionsByDateHandler`.
- **Daily Balance**: `ApplyTransactionCommand`/`ApplyTransactionHandler` (chamado pelo
  consumidor MassTransit) e `GetDailyBalanceQuery`/`GetDailyBalanceHandler` (cache-aside).

Não há um mediador genérico (tipo MediatR): cada caso de uso é uma classe explícita
registrada por interface (`ICommandHandler<TCommand,TResult>` /
`IQueryHandler<TQuery,TResult>`), injetada diretamente no controller correspondente. Essa é
a aplicação pragmática de CQRS mencionada no desafio: **separação clara entre o caminho de
escrita (Ledger) e o caminho de leitura por projeção (Daily Balance)**, sem introduzir
complexidade adicional (barramento de mensagens em memória, pipelines de handler
genéricos) que este tamanho de solução não justifica.

### `Infrastructure`

Implementações técnicas: EF Core (`LedgerDbContext`/`DailyBalanceDbContext`, com
`EFCore.NamingConventions` para snake_case), repositórios, Outbox/Inbox, MassTransit +
RabbitMQ, Redis (`StackExchange.Redis`), métricas customizadas (`LedgerMetrics`,
`DailyBalanceCacheMetrics`).

- **Ledger**: `TransactionRepository`, `OutboxWriter`, `UnitOfWork` (traduz violação de
  índice único de `idempotency_key` em `IdempotencyConflictException`),
  `OutboxPublisherService` (BackgroundService que faz polling da Outbox e publica via
  `IPublishEndpoint`).
- **Daily Balance**: `DailyBalanceRepository`, `ProcessedMessageStore`, `UnitOfWork` (traduz
  violação de índice único de `processed_messages.event_id` em `DuplicateEventException`),
  `RedisDailyBalanceCache` (cache-aside com fallback silencioso ao Postgres em caso de falha
  do Redis), `TransactionRegisteredConsumer` (consumidor MassTransit).

### `Api`

Controllers REST, middleware de Correlation ID, autenticação JWT Bearer, rate limiting,
Problem Details para erros, Swagger/OpenAPI e health checks. Traduz requisições HTTP em
comandos/queries de Application e DTOs de Application em respostas HTTP.

- **Ledger.Api**: `TransactionsController` (`POST`/`GET /api/v1/transactions`).
- **DailyBalance.Api**: `DailyBalancesController` (`GET /api/v1/daily-balances/{date}`).
- **DailyBalance.Worker**: não tem controllers de negócio — apenas os endpoints de health
  check (`/health/live`, `/health/ready`) e o host do consumidor MassTransit em background.

## Referências

- [05 — Fluxos principais](05-fluxos-principais.md)
- [06 — Modelo de dados](06-modelo-de-dados.md)
- [ADR-001 — Separação de contextos](../adr/001-separacao-de-contextos-ledger-e-daily-balance.md)
