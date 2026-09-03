# ADR 003 - Transactional Outbox e Inbox
Status: Aceita
Data: 2026-09-02

## Contexto

Com a integração assíncrona definida ([ADR-002](002-integracao-assincrona-por-eventos.md)),
surgem dois problemas clássicos de mensageria: (1) a janela de falha entre persistir o
lançamento e publicar o evento — se o processo cair entre as duas operações, o evento se
perde ou fica órfão; e (2) o RabbitMQ (via MassTransit) garante entrega **at-least-once**, o
que significa que o Daily Balance Worker pode receber a mesma mensagem mais de uma vez.

## Decisão

Aplicar o padrão **Transactional Outbox** no Ledger (produtor) e o padrão **Inbox** no Daily
Balance (consumidor).

**Outbox**: ao registrar um lançamento, o Ledger grava a linha em `transactions` e a
mensagem correspondente em `outbox_messages` **na mesma transação de banco**
(`RegisterTransactionHandler` + `IUnitOfWork.SaveChangesAsync`). Isso elimina a janela de
falha: ou as duas gravações acontecem juntas, ou nenhuma acontece. Um processo separado
(`OutboxPublisherService`) faz polling da tabela e publica as mensagens pendentes,
marcando `published_at` só após confirmação — se ele publicar mas cair antes de marcar, a
mensagem será publicada de novo no próximo ciclo (é aqui que a Inbox entra).

**Inbox**: ao consumir um evento, o Daily Balance Worker verifica primeiro se o `EventId`
já existe em `processed_messages`. Se existir, o processamento é um no-op. Se não existir,
aplica o efeito à projeção e grava a marca de processamento **na mesma transação** da
atualização de `daily_balances`.

## Alternativas consideradas

- **Publicar direto após o `SaveChanges` do lançamento, sem Outbox**: descartada — reintroduz
  a janela de falha entre persistir e publicar (o processo pode cair entre as duas
  operações, perdendo o evento silenciosamente).
- **Consumir e aplicar sem Inbox, confiando em UPSERT idempotente por si só**: um UPSERT
  incremental (`total_credits += amount`) não é naturalmente idempotente — aplicá-lo duas
  vezes para o mesmo evento dobra o valor. Sem uma tabela de deduplicação, não há como
  distinguir "eventos diferentes com o mesmo efeito" de "o mesmo evento entregue duas vezes".
- **Outbox + Inbox** (escolhida): resolve os dois problemas com um mecanismo simples,
  auditável (dá para ver exatamente o que está pendente e o que já foi processado) e sem
  dependência de infraestrutura adicional (usa o próprio PostgreSQL de cada serviço).

## Consequências positivas

- Nenhum evento confirmado (lançamento persistido) é perdido entre o banco e a mensageria —
  ele fica durável em `outbox_messages` até ser publicado com sucesso.
- O broker pode reentregar mensagens (comportamento at-least-once, normal e esperado) sem que
  isso duplique o efeito no saldo: a Inbox garante que a aplicação da mudança de estado
  aconteça **no máximo uma vez por EventId** — o que chamamos de *exactly-once effect*, não
  de exactly-once delivery (o RabbitMQ nunca garante isso, e não afirmamos que garanta).
- O padrão é observável: `outbox_messages.published_at IS NULL AND dead_lettered_at IS NULL`
  mostra exatamente o que está pendente de publicação (mensagens com falha permanente ficam
  marcadas em `dead_lettered_at` e saem dessa contagem — ver
  [resiliency-and-messaging.md](../resiliency-and-messaging.md)); `processed_messages` mostra
  exatamente o que já foi aplicado.

## Consequências negativas e mitigações

- **Latência adicional**: o evento só é publicado no próximo ciclo de polling do Outbox
  Publisher (padrão 2s), não instantaneamente. Mitigação: aceitável dado que o sistema já
  assume consistência eventual entre Ledger e Daily Balance; o intervalo é configurável
  (`OutboxPublisher:PollingInterval`).
- **Duas tabelas técnicas adicionais** por serviço, que crescem ao longo do tempo e precisam
  de manutenção (ex.: arquivamento de `processed_messages` antigas). Mitigação: fora do
  escopo do MVP, mas descrito como ponto de atenção operacional em
  [operational-runbook.md](../operational-runbook.md); não implementamos rotina de limpeza
  automática.
- **Falha de publicação pode ficar "invisível"** sem monitoramento ativo. Mitigação: métrica
  `verity.ledger.outbox.pending` (ver [observability.md](../observability.md)) e alerta
  sugerido para Outbox acumulada.

## Critérios de revisão

Revisar se o volume de eventos crescer a ponto de o polling da Outbox se tornar um gargalo
(nesse caso, avaliar Change Data Capture/Debezium como alternativa ao polling) ou se
`processed_messages` crescer sem controle e precisar de uma estratégia de retenção/particionamento.
