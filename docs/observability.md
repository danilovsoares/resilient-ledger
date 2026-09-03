# Observabilidade

## Objetivo do documento

Documentar como a solução é observada: correlação de requisições, logs estruturados, métricas
e tracing — e mostrar como uma equipe rastrearia uma jornada de negócio de ponta a ponta.

## Escopo

Ledger API, Daily Balance API e Daily Balance Worker. Todos os três compartilham a mesma
abordagem (Serilog + OpenTelemetry), configurada individualmente em cada `Program.cs`.

## Middleware de `X-Correlation-ID`

`CorrelationIdMiddleware` (presente na Ledger API e na Daily Balance API) executa antes de
qualquer outro middleware de negócio:

1. Lê o header `X-Correlation-ID` do request. Se ausente ou não for um GUID válido, gera um novo
   (`Guid.NewGuid()`).
2. Publica o valor em `HttpContext.Items` (para uso pelos controllers) e no contexto de log do
   Serilog (`LogContext.PushProperty("CorrelationId", ...)`) — isso faz o `CorrelationId`
   aparecer automaticamente em **todo** log emitido durante o processamento daquele request,
   sem precisar passá-lo explicitamente a cada chamada de log.
3. Devolve o mesmo valor no header `X-Correlation-ID` da resposta, para o cliente poder
   reportá-lo em caso de suporte.

## Propagação em mensageria

Ao publicar o evento pela Outbox, `OutboxPublisherService` propaga `CorrelationId` como
`PublishContext.CorrelationId` (reconhecido nativamente pelo MassTransit) e `CausationId` como
header customizado da mensagem. Ao consumir, o `TransactionRegisteredConsumer` lê o
`CorrelationId` diretamente do payload do evento (`TransactionRegisteredEvent.CorrelationId`) e
o grava em `processed_messages.correlation_id`, além de incluí-lo nos logs estruturados do
consumo.

## Diferença entre CorrelationId, CausationId, EventId, TraceId e SpanId

| Campo | Significado | Escopo |
|---|---|---|
| `CorrelationId` | Identifica uma jornada de negócio inteira, do request HTTP ao processamento assíncrono correspondente. | Toda a jornada (pode atravessar múltiplos processos/mensagens). |
| `CausationId` | Identifica a ação/mensagem imediatamente anterior que causou o evento atual. Neste escopo (sem cadeias de eventos causando outros eventos), é igual ao `CorrelationId` da requisição original — ver [ADR-005](adr/005-observabilidade-e-correlation-id.md). | Um passo da jornada. |
| `EventId` | Identidade única do evento de integração (`TransactionRegisteredEvent.EventId`), usada como chave de deduplicação na Inbox. | Um evento específico. |
| `TraceId` / `SpanId` | Identificadores de rastreamento distribuído do OpenTelemetry — um `TraceId` agrupa os `SpanId`s de todas as operações técnicas (HTTP, SQL, publish/consume) de uma mesma requisição/consumo. | Uma árvore de spans técnicos. |

`TraceId`/`SpanId` são anexados automaticamente a cada log estruturado via
`Serilog.Enrichers.Span` (`.Enrich.WithSpan()`), permitindo saltar diretamente do log para o
trace correspondente no backend de tracing configurado.

## Logs estruturados (Serilog)

Todos os três serviços usam Serilog com saída em JSON compacto
(`Serilog.Formatting.Compact.CompactJsonFormatter`) — logs nunca são concatenados como texto
livre; cada entrada é um objeto estruturado, filtrável por campo. `UseSerilogRequestLogging()`
gera automaticamente uma entrada por request HTTP com o método, path, status code e tempo de
resposta (`ElapsedMs`).

Campos presentes em toda entrada de log (via enrichers globais):

- `Service` (nome do serviço: `verity-ledger-api`, `verity-daily-balance-api`,
  `verity-daily-balance-worker`)
- `Environment` (ambiente ASP.NET Core: `Development`, `Production`, etc.)
- `TraceId`, `SpanId` (via `Serilog.Enrichers.Span`)
- `CorrelationId` (via `CorrelationIdMiddleware`, presente em toda entrada emitida durante o
  processamento de um request HTTP)

Campos presentes nas entradas específicas de cada operação (como propriedades nomeadas nos
templates de mensagem de log, ex.: `logger.LogInformation("Evento {EventId} recebido para
TransactionId {TransactionId}...", ...)`):

- `EventId`, `TransactionId`, `BusinessDate` — nos logs de registro/consumo de lançamento.
- `MessageId` — identidade da mensagem no nível de transporte do MassTransit
  (`ConsumeContext.MessageId`), logada no consumo do evento. Distinta do `EventId` de negócio
  (que vive dentro do payload), mas `OutboxPublisherService` fixa `context.MessageId` com o
  mesmo valor do `EventId` ao publicar — em vez de deixar o MassTransit gerar um identificador
  aleatório — para que os dois campos sejam intercambiáveis ao investigar uma mensagem
  específica.
- `Exception` — anexado automaticamente pelo Serilog quando um log é emitido com uma exceção
  (`LogError(ex, ...)`, `LogWarning(ex, ...)`).
- `OperationName` — corresponde ao nome do endpoint/handler, visível nos logs de
  `UseSerilogRequestLogging` (`RequestPath`) e nas mensagens estruturadas de cada handler.

## Métricas

Coletadas via OpenTelemetry (`OpenTelemetry.Instrumentation.AspNetCore`,
`.Instrumentation.Http`) e exportadas via OTLP quando `OpenTelemetry:OtlpEndpoint` está
configurado (ex.: para um Collector ou diretamente para o Datadog Agent em produção):

- **Automáticas** (instrumentação padrão do ASP.NET Core): taxa de requisições, taxa de erro
  (por status code) e latência (histograma, do qual p50/p95/p99 são derivados) por endpoint —
  `http.server.request.duration`.
- **Customizadas**:
  - `verity.ledger.outbox.pending` (gauge, `Verity.Ledger.Infrastructure.Telemetry.LedgerMetrics`):
    quantidade de mensagens em `outbox_messages` aguardando publicação (`published_at IS NULL
    AND dead_lettered_at IS NULL`) — a base para o alerta de "Outbox acumulada" (ver
    [ADR-003](adr/003-transactional-outbox-e-inbox.md)). Mensagens dead-lettered (falha
    permanente — tipo de evento desconhecido ou payload corrompido, ver
    [resiliency-and-messaging.md](resiliency-and-messaging.md)) não entram nesta contagem; hoje
    só ficam visíveis via log `Error` do `OutboxPublisherService` no momento em que acontecem —
    não há métrica/alerta dedicado para elas.
  - `verity.dailybalance.cache.hits` / `verity.dailybalance.cache.misses` (contadores,
    `Verity.DailyBalance.Infrastructure.Telemetry.DailyBalanceCacheMetrics`): a razão de cache
    hit (`hits / (hits + misses)`) é derivada destes dois contadores pelo backend de
    observabilidade — não é publicada como uma métrica pré-calculada.

Métricas relacionadas a mensagens na fila, retries e DLQ **não são emitidas como métricas
customizadas da aplicação** neste repositório — elas são observáveis operacionalmente pela
management UI do RabbitMQ (profundidade de fila, taxa de redelivery) e pelos spans de tracing do
MassTransit (`AddSource("MassTransit")`), que marcam falhas e tentativas de retry. Isso é uma
limitação conhecida, não uma alegação de cobertura completa — ver
[future-evolution.md](future-evolution.md) para a evolução sugerida (exportar métricas nativas
do RabbitMQ/MassTransit para o mesmo backend).

## Tracing

`AddAspNetCoreInstrumentation()` (requests HTTP recebidos), `AddHttpClientInstrumentation()`
(chamadas HTTP saintes, se houver), `AddSource("Npgsql")` (queries PostgreSQL — Npgsql emite
sua própria ActivitySource nativamente) e `AddSource("MassTransit")` (publish/consume de
mensagens) compõem o `TracerProvider` de cada serviço. Em conjunto, um único `TraceId` cobre o
ciclo `POST /api/v1/transactions` → `INSERT` no PostgreSQL do Ledger — mas **não** atravessa a
fronteira assíncrona até o consumo no Daily Balance Worker, porque não há propagação de contexto
de trace do W3C através das mensagens do RabbitMQ configurada neste repositório. A ligação entre
o trace do Ledger e o trace do consumo no Worker é feita pelo `CorrelationId` (que, ao contrário
do `TraceId`, é propagado explicitamente no payload do evento), não pelo `TraceId`.

## Alertas sugeridos (não implementados)

Nenhum sistema de alerta está configurado neste repositório — não há ambiente de produção para
alertar. Os alertas abaixo são a recomendação natural, dadas as métricas já expostas:

- Aumento sustentado da taxa de erro 5xx nas duas APIs.
- p95 de latência acima da meta (300ms no teste de carga, ver
  [performance-and-capacity.md](performance-and-capacity.md)) por um período sustentado.
- `verity.ledger.outbox.pending` crescendo continuamente (indica broker fora do ar ou
  publicador travado).
- Fila de erro (`_error`) do RabbitMQ com profundidade maior que zero.
- Ausência de atividade do consumidor (nenhuma mensagem processada) por um período acima do
  esperado, dado o volume histórico.

## Exemplo de jornada rastreável por CorrelationId

1. `POST /api/v1/transactions` chega sem `X-Correlation-ID` — o middleware gera
   `c1c1c1c1-...`. A resposta 201 traz esse valor no header.
2. O log de `UseSerilogRequestLogging` para esse request carrega `CorrelationId=c1c1c1c1-...`.
3. `RegisterTransactionHandler` grava `outbox_messages.correlation_id = c1c1c1c1-...` na mesma
   transação do lançamento.
4. `OutboxPublisherService` publica o evento minutos (ou segundos) depois, com log estruturado
   contendo o mesmo `CorrelationId`.
5. `TransactionRegisteredConsumer`, no Worker, recebe o evento e loga
   `CorrelationId=c1c1c1c1-...` ao processá-lo.
6. `processed_messages.correlation_id = c1c1c1c1-...` fica gravado permanentemente, ligando a
   linha da Inbox de volta ao request HTTP original.

Uma consulta nos logs estruturados (ou uma query em `outbox_messages`/`processed_messages`) por
`c1c1c1c1-...` reconstrói a jornada inteira, do clique do comerciante até a atualização do saldo.

## Referências

- [ADR-005 — Observabilidade e Correlation ID](adr/005-observabilidade-e-correlation-id.md)
- [Resiliência e mensageria](resiliency-and-messaging.md)
- [Runbook operacional](operational-runbook.md)
