# Mensageria e Resiliência

## Objetivo

Detalhar as garantias reais de entrega, o funcionamento da Outbox e da Inbox, a política de
retry, o comportamento de DLQ e como a solução se comporta diante de cada falha de
infraestrutura plausível.

## Escopo

Comportamento de runtime da integração Ledger → RabbitMQ → Daily Balance. Para a decisão e as
alternativas consideradas, ver os ADRs 002, 003 e 004.

## Garantias de entrega

RabbitMQ, via MassTransit, garante entrega **at-least-once**: uma mensagem publicada será
entregue ao menos uma vez, mas pode ser entregue mais de uma vez (reconexões, timeouts de ACK,
redelivery após falha do consumidor). **Não afirmamos que o RabbitMQ garante exactly-once
delivery** — nenhum broker de mensagens tradicional garante isso de forma genérica, e afirmar
o contrário seria falso.

O que a solução garante é **exactly-once effect** no banco de projeção do Daily Balance: não
importa quantas vezes um evento seja entregue, seu efeito no saldo é aplicado no máximo uma
vez. Isso é obtido por deduplicação transacional (Inbox), não pelo broker.

## Outbox: publicador, tentativas e marca de publicação

`OutboxPublisherService` (BackgroundService na Ledger API) faz polling de
`outbox_messages WHERE published_at IS NULL AND dead_lettered_at IS NULL` a cada 2 segundos (padrão,
`OutboxPublisher:PollingInterval`), em lotes de até 50 mensagens
(`OutboxPublisher:BatchSize`). Para cada mensagem:

1. Desserializa o payload conforme o `Type` gravado.
2. Publica via `IPublishEndpoint.Publish`, propagando `CorrelationId`/`CausationId`.
3. Em sucesso, marca `published_at = now()`.
4. Em falha **transitória** (ex.: RabbitMQ indisponível), incrementa `retry_count`, grava
   `last_error` e **deixa a mensagem pendente** — ela será tentada novamente no próximo
   ciclo, indefinidamente, até ser publicada com sucesso. Não há limite de tentativas para
   esse caso: a Outbox é, por definição, a garantia de que o evento *será* publicado
   eventualmente; abandonar essa tentativa violaria essa garantia.
5. Em falha **permanente** (tipo de evento não registrado em `OutboxEventTypeRegistry`, ou
   payload que não desserializa) — um erro que retentar nunca vai resolver, porque nada muda
   entre ciclos —, a mensagem é marcada como dead-lettered (`dead_lettered_at = now()`) já na
   primeira ocorrência e para de ser selecionada pelo publicador. A distinção entre os dois
   casos é feita pelo tipo da exceção (`InvalidOperationException`/`JsonException` na etapa de
   resolução do tipo e desserialização = permanente; qualquer falha na chamada de `Publish` em
   si = transitória), não por um contador de tentativas — um teto genérico de `retry_count`
   marcaria como permanente uma indisponibilidade prolongada do broker, que é exatamente o
   cenário que este mecanismo existe para tolerar.

Isso é observável diretamente: `SELECT count(*) FROM outbox_messages WHERE published_at IS
NULL AND dead_lettered_at IS NULL` mostra o backlog pendente, e é a base da métrica
`verity.ledger.outbox.pending` (ver [observability.md](observability.md)). Mensagens
dead-lettered (`dead_lettered_at IS NOT NULL`) requerem investigação manual — ver `last_error`
na própria linha — e não são reprocessadas automaticamente.

## Inbox: deduplicação por EventId

`ApplyTransactionHandler`, no Daily Balance, consulta `processed_messages` pelo `EventId`
**antes** de tocar em qualquer estado. Se o `EventId` já existe, a operação é um no-op —
nenhuma linha de `daily_balances` é alterada. Se não existe, a atualização da projeção e o
`INSERT` em `processed_messages` acontecem **na mesma transação de banco**
(`IUnitOfWork.SaveChangesAsync`), então não existe uma janela em que o saldo foi atualizado
mas a marca de processamento não foi gravada (ou vice-versa).

## ACK somente após commit local

O MassTransit só confirma (ACK) a mensagem ao RabbitMQ depois que `Consume` retorna sem
lançar exceção — ou seja, depois que a transação de banco (projeção + Inbox) já foi
commitada. Se o processo do Worker cair entre o commit e o ACK, o broker reentrega a
mensagem; como o `EventId` já está gravado, o reprocessamento é absorvido pela Inbox sem
duplicar efeito.

## Retry exponencial com jitter

O consumidor está configurado com `UseMessageRetry(retry => retry.Exponential(...))`:
5 tentativas, intervalo inicial de 200ms, incremento de 200ms, teto de 10 segundos. Isso dá
ao sistema tempo para se recuperar de falhas transitórias (ex.: uma reconexão momentânea ao
PostgreSQL) sem intervenção manual, antes de considerar a mensagem uma falha persistente.

## DLQ: quando usar, como diagnosticar, como reprocessar

Se as 5 tentativas de retry se esgotarem, o MassTransit encaminha a mensagem para a fila de
erro associada ao receive endpoint (convenção de nome `{queue}_error`, criada
automaticamente pelo transporte RabbitMQ) — essa é a DLQ desta solução. A fila principal
continua consumindo as próximas mensagens normalmente; uma mensagem "presa" não bloqueia as
demais.

- **Quando usar**: quando `retry_count` (ou os logs do consumidor) mostrarem falhas
  persistentes para uma mensagem específica, e não transitórias.
- **Como diagnosticar**: inspecionar a mensagem na fila `_error` pela management UI do
  RabbitMQ (exposta localmente em `http://localhost:15673`) — o corpo e os headers da
  mensagem original ficam preservados, incluindo `CorrelationId`.
- **Como reprocessar sem duplicar saldo**: mover a mensagem de volta para a fila original
  (ex.: via management UI ou uma ferramenta de shovel) é seguro, porque o `EventId` daquela
  mensagem **nunca foi gravado em `processed_messages`** (ela falhou antes de completar a
  transação) — reprocessá-la aplica o efeito pela primeira vez, não duplica nada.

## Eventos fora de ordem

No modelo atual (aditivo — inclusive o cancelamento é aditivo, ver abaixo), processar eventos
fora de ordem é seguro — ver [ADR-004](adr/004-consistencia-eventual-e-ordenacao.md).
Cancelamento (`POST /api/v1/transactions/{id}/cancel`) registra um estorno como um novo
lançamento, em vez de mutar o original, então não introduz nenhuma dependência de ordem. Para
uma evolução futura que suporte **edição** de verdade (mutar um lançamento existente), a
estratégia planejada (não implementada) é anexar `TransactionId` + `EventVersion` a cada evento,
com o consumidor ignorando versões antigas.

## Atualização atômica e race conditions

`DailyBalance.Apply` é sempre executado dentro do mesmo ciclo de leitura-modificação-escrita
protegido pela transação do `UnitOfWork`; a corrida entre dois consumidores processando o
mesmo `EventId` simultaneamente é resolvida pela violação de chave primária em
`processed_messages` (`DuplicateEventException`) — o perdedor da corrida tem sua transação
inteira revertida (incluindo o incremento de saldo que ele havia calculado em memória), então
não há como o mesmo evento incrementar o saldo duas vezes mesmo sob concorrência real.

## Cenários de falha

| Falha | Efeito esperado | Mecanismo de proteção | Ação operacional |
|---|---|---|---|
| Daily Balance indisponível | Ledger continua aceitando lançamentos normalmente | Outbox + broker (sem chamada síncrona) | Restaurar o consumidor e acompanhar o backlog da fila |
| RabbitMQ indisponível | Lançamento confirmado ao cliente; evento fica pendente na Outbox | Outbox Publisher com retry indefinido | Monitorar `outbox_messages` não publicadas (métrica `verity.ledger.outbox.pending`) |
| Worker cai antes do ACK | Mensagem pode ser reentregue | Inbox idempotente (`processed_messages`) | Nenhuma correção manual esperada |
| Mensagem inválida/malformada | Não bloqueia a fila principal | Retry limitado (5 tentativas) + DLQ (`_error`) | Corrigir a causa raiz e reprocessar a partir da DLQ |
| Redis indisponível | Consulta de saldo mais lenta, mas funcional | Fallback ao PostgreSQL (`RedisDailyBalanceCache`) | Restaurar o Redis e observar a latência da consulta voltar ao normal |

## Referências

- [ADR-002 — Integração assíncrona por eventos](adr/002-integracao-assincrona-por-eventos.md)
- [ADR-003 — Transactional Outbox e Inbox](adr/003-transactional-outbox-e-inbox.md)
- [ADR-004 — Consistência eventual e ordenação](adr/004-consistencia-eventual-e-ordenacao.md)
- [Runbook operacional](operational-runbook.md)
- [Observabilidade](observability.md)
