# Estratégia de Testes

## Objetivo do documento

Definir a pirâmide de testes adotada, os cenários obrigatórios cobertos e por que Testcontainers
é usado para provar comportamento com dependências reais sem exigir um ambiente externo
provisionado manualmente.

## Escopo

Quatro projetos de teste, xUnit + FluentAssertions:

```text
tests/
├── Verity.Ledger.UnitTests              # Domain, Application, GlobalExceptionHandler, health checks
├── Verity.Ledger.IntegrationTests       # Api + Postgres real; Messaging/ roda contra RabbitMQ real
├── Verity.DailyBalance.UnitTests        # Domain, Application, GlobalExceptionHandler
└── Verity.DailyBalance.IntegrationTests # Api + Postgres/Redis reais; Messaging/ roda contra RabbitMQ real
```

## Pirâmide de testes

- **Testes unitários** (`*.UnitTests`): domínio e handlers de Application, com dependências de
  Infrastructure substituídas por fakes/mocks. Rápidos (sem I/O real), executados a cada build.
- **Testes de integração** (`*.IntegrationTests`): sobem dependências reais via Testcontainers
  (PostgreSQL, Redis e, onde necessário para provar comportamento de broker, RabbitMQ) e hospedam
  a Api real via `WebApplicationFactory<Program>` (ou, para o pipeline de mensageria, a mesma
  composição de DI do Worker real via `Host.CreateDefaultBuilder`), exercitando o caminho
  completo até o banco.
  - A maior parte dos testes de Inbox/idempotência chama `ApplyTransactionHandler` diretamente
    via DI (sem passar pelo RabbitMQ), porque a garantia de deduplicação vive na camada de
    persistência, não no transporte — ver `DailyBalanceApiFactory`.
  - Adicionalmente, `Messaging/OutboxPublisherPipelineTests` (Ledger) e
    `Messaging/TransactionRegisteredConsumerPipelineTests` (Daily Balance) rodam contra um
    RabbitMQ real (Testcontainers.RabbitMq), provando que o `OutboxPublisherService` publica de
    verdade e marca `published_at`, que o `TransactionRegisteredConsumer` real (não o handler
    chamado à mão) consome, aplica o efeito e deduplica reentrega vindas do broker de fato — e
    que uma falha persistente (PostgreSQL real derrubado durante o teste) esgota o retry
    configurado e encaminha a mensagem para a fila de erro (DLQ), não apenas da camada de
    persistência isolada.
- Não há, ainda, um único teste automatizado que una as duas pontas (o Ledger publicando E o
  Daily Balance consumindo no mesmo processo de teste, através do mesmo broker) — cada serviço
  prova sua metade do pipeline real separadamente. A validação da cadeia completa, ponta a ponta,
  foi feita manualmente via `docker compose up` (comandos e resultados no
  [README](../README.md)) e com o script de carga k6.

## Por que Testcontainers

Os testes de integração usam contêineres Docker efêmeros (PostgreSQL, RabbitMQ) criados e
destruídos automaticamente pelo próprio processo de teste (`Testcontainers.PostgreSql`,
`Testcontainers.RabbitMq`), em vez de depender de uma instância compartilhada pré-provisionada.
Isso prova o comportamento contra o motor de banco e o broker reais (não um in-memory
substituto que pode mascarar diferenças de comportamento SQL ou de mensageria) e roda em
qualquer máquina com Docker instalado, incluindo o runner do GitHub Actions
(`.github/workflows/ci.yml`), sem exigir infraestrutura externa provisionada manualmente.

## Cenários obrigatórios e onde estão cobertos

| Cenário | Camada | Racional |
|---|---|---|
| Regras de domínio (`Transaction.Register`, `DailyBalance.Apply`) | Unit | Validam invariantes (valor positivo, chave de idempotência obrigatória) sem depender de infraestrutura. |
| Validações de entrada (`RegisterTransactionValidator`) | Unit | FluentValidation é testável isoladamente, sem subir a Api. |
| Persistência + Outbox (lançamento e evento gravados na mesma transação) | Integration (Ledger, Postgres real) | Só é possível provar atomicidade real inspecionando as tabelas após uma transação de banco de verdade. |
| Indisponibilidade do broker/consolidado | Integration (Ledger, Postgres real + RabbitMQ apontado para um host inexistente) | Prova a garantia central do desafio: o Ledger continua aceitando lançamentos com o broker fora do ar. |
| Reentrega (Inbox não duplica saldo) | Integration (Daily Balance, Postgres real; e, em `TransactionRegisteredConsumerPipelineTests`, RabbitMQ real) | Publicar/aplicar o mesmo `EventId` duas vezes e conferir que o saldo reflete o efeito uma única vez exige o banco real e a restrição de unicidade — validado tanto chamando o handler diretamente quanto através do consumidor MassTransit real. |
| Publicação real da Outbox | Integration (Ledger, `OutboxPublisherPipelineTests`, RabbitMQ real) | Confirma que `OutboxPublisherService` efetivamente publica no broker e marca `published_at` — não apenas que a mensagem foi gravada na tabela. |
| Falha permanente na Outbox não é retentada para sempre, falha transitória nunca vira dead-letter | Integration (Ledger, `OutboxDeadLetterTests`) | Prova as duas metades da correção de retry infinito: um tipo de evento desconhecido marca `dead_lettered_at` já na 1ª tentativa e para de ser selecionado; uma falha de broker (transitória) nunca marca `dead_lettered_at`. |
| Ordem de eventos (aditividade) | Unit (Daily Balance) | Aplicar débito-antes-de-crédito e crédito-antes-de-débito deve chegar ao mesmo saldo final. |
| DLQ / retry esgotado | Integration (Daily Balance, `TransactionRegisteredConsumerPipelineTests`, RabbitMQ real) | Derruba o PostgreSQL real durante o teste, forçando toda tentativa de consumo a falhar de verdade; confirma, consultando a fila `transaction-registered_error` via `RabbitMQ.Client`, que a mensagem chega lá após esgotar as 5 tentativas de `UseMessageRetry`. |
| Propagação de CorrelationId | Integration (Ledger e Daily Balance, incluindo através do RabbitMQ real em `TransactionRegisteredConsumerPipelineTests`) | Confirma que o valor do header/evento chega até as colunas `outbox_messages.correlation_id` e `processed_messages.correlation_id`. |
| Estorno: registra lançamento de tipo oposto e mesmo valor | Unit (`Transaction.RegisterReversal`, `CancelTransactionHandlerTests`) + Integration (`POST .../cancel`, Postgres real) | Prova que o original nunca é mutado — o efeito é zerado por um novo lançamento aditivo, não por edição (ver ADR-004). |
| Estorno: não permite estornar o mesmo lançamento duas vezes | Unit + Integration | `HasReversalAsync` bloqueia o segundo estorno com 400; a checagem é uma consulta de existência, não um estado mutável gravado no lançamento original. |
| Consulta reflete lançamentos estornados | Unit (`GetTransactionsByDateHandlerTests`) + Integration | `reversedByTransactionId` é calculado a cada consulta (`GetReversalMapAsync`), inclusive quando o estorno foi registrado em uma data de negócio diferente da do lançamento original. |
| Login: credenciais válidas emitem token válido no endpoint protegido | Unit (`LoginHandlerTests`) + Integration (`AuthEndpointTests`, Postgres real) | Valida a regra (`LoginHandler`) isoladamente e, ponta a ponta, que o token emitido por `/api/v1/auth/login` é aceito por `/api/v1/transactions`. |
| Login: senha incorreta e usuário inexistente respondem de forma indistinguível | Unit + Integration | `LoginHandlerTests` prova que o hasher não é sequer chamado para usuário inexistente; `AuthEndpointTests` prova que a resposta HTTP (título/detalhe) é idêntica nos dois casos — não vaza quais usuários existem. |
| Validação de usuário/senha (`LoginValidator`) | Unit (`LoginValidatorTests`) | Espelha os limites reais: `Username` até 128 (`users.username`), `Password` até 72 (limite efetivo do BCrypt). |

## Execução

```bash
dotnet test Verity.slnx
```

Ou por projeto, quando só a camada unitária é necessária (mais rápido, sem Docker):

```bash
dotnet test tests/Verity.Ledger.UnitTests
dotnet test tests/Verity.DailyBalance.UnitTests
```

Os testes de integração exigem Docker rodando na máquina (Testcontainers gerencia o resto
automaticamente — não é necessário subir `docker-compose.yml` antes de rodar os testes; os
contêineres de teste são isolados dos contêineres da aplicação).

## Cobertura de código

Medida com `coverlet.collector` (já referenciado nos 4 projetos de teste) e consolidada com
`reportgenerator`:

```bash
dotnet test Verity.slnx -c Release --collect:"XPlat Code Coverage" --results-directory ./coverage-results
reportgenerator -reports:"coverage-results/**/coverage.cobertura.xml" -targetdir:"coverage-report" -reporttypes:Html
```

Execução de referência (76 testes, todos passando): **95,5% de cobertura de linha**, 95,6% de
cobertura de método. Domínio e Application dos dois serviços estão em 100%; os pontos abaixo de
100% concentram-se em código de composição/infraestrutura de baixo risco — `Program.cs`
(caminhos de inicialização condicionais), health checks (exercitados manualmente via
`docker compose`, não por teste automatizado) e ramos defensivos do `GlobalExceptionHandler` para
exceções que não ocorrem nos cenários testados. `DefaultUserSeeder` está em 0% (só roda uma vez,
na subida do container, gated por `Auth:SeedDefaultUser` — exercitado manualmente, não por teste
automatizado; ver [security.md](security.md)).

Este número não é um alvo formal do desafio (que não especifica um percentual de cobertura); é
reportado aqui como evidência objetiva de que a lógica de negócio crítica (Domain, Application,
Outbox, Inbox, consumo real via broker) está coberta, não apenas implementada.

## CI

`.github/workflows/ci.yml` roda build + testes unitários em todo push/PR, seguido de um job
separado de testes de integração (o runner `ubuntu-latest` do GitHub Actions já tem Docker
disponível nativamente para o Testcontainers). Um terceiro job roda os testes do frontend
(`npm test`, Vitest) e o build de produção do Angular.

## Referências

- [Resiliência e mensageria](resiliency-and-messaging.md)
- [Requisitos não funcionais](non-functional-requirements.md)
- [Performance e capacidade](performance-and-capacity.md)
