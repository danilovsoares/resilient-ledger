# ADR 002 - Integração assíncrona por eventos
Status: Aceita
Data: 2026-09-02

## Contexto

Com Ledger e Daily Balance como serviços separados ([ADR-001](001-separacao-de-contextos-ledger-e-daily-balance.md)),
é preciso decidir como o segundo fica sabendo dos lançamentos registrados pelo primeiro, sem
comprometer a disponibilidade do caminho de escrita.

## Decisão

Usar **RabbitMQ + MassTransit** para a integração: o Ledger publica um evento de integração
(`TransactionRegisteredEvent`) sempre que um lançamento é registrado; o Daily Balance Worker
consome esse evento de forma assíncrona e atualiza sua projeção. O Ledger nunca aguarda
resposta do Daily Balance.

## Alternativas consideradas

- **Chamada HTTP síncrona** do Ledger para o Daily Balance após persistir o lançamento:
  descartada porque transformaria a disponibilidade do consolidado em dependência direta do
  caminho crítico de escrita — exatamente o que o requisito não funcional do desafio proíbe.
  Mesmo com timeout curto e circuit breaker, uma chamada síncrona introduz uma janela em que
  o Ledger fica mais lento ou falha por causa de um problema alheio ao seu próprio domínio.
- **Polling do Daily Balance sobre o banco do Ledger**: descartada por acoplar os dois bancos
  de dados (violaria o isolamento de dados por contexto) e por introduzir latência
  proporcional ao intervalo de polling sem nenhum ganho sobre uma fila de mensagens.
- **RabbitMQ + MassTransit** (escolhida): desacopla temporalmente (o Daily Balance não
  precisa estar de pé no momento da publicação) e desacopla disponibilidade (uma falha no
  broker ou no consumidor não bloqueia a escrita — ver
  [ADR-003](003-transactional-outbox-e-inbox.md) para como isso é garantido mesmo com o
  broker fora do ar).

## Consequências positivas

- Disponibilidade do Ledger independente do Daily Balance e do próprio broker (a Outbox
  absorve a indisponibilidade do RabbitMQ — ver ADR-003).
- Backpressure natural: se o Daily Balance Worker cair ou ficar lento, mensagens se acumulam
  na fila em vez de sobrecarregar ou derrubar o Ledger.
- MassTransit fornece, prontos, os mecanismos de retry com backoff e roteamento para fila de
  erro (DLQ) usados no fluxo de falha de processamento (ver
  [05-fluxos-principais.md](../architecture/05-fluxos-principais.md#6-falha-de-processamento-retry-e-dlq)).

## Consequências negativas e mitigações

- **RabbitMQ garante entrega at-least-once, não exactly-once** — uma mensagem pode ser
  entregue mais de uma vez. Mitigação: Inbox no consumidor (ver ADR-003) torna o efeito da
  reentrega idempotente.
- Introduz um componente de infraestrutura adicional (o broker) a operar e monitorar.
  Mitigação: health check dedicado, management UI exposta localmente para diagnóstico, e
  cenários de falha documentados em [resiliency-and-messaging.md](../resiliency-and-messaging.md).
- Depuração de um fluxo assíncrono é mais trabalhosa do que uma chamada síncrona.
  Mitigação: CorrelationId propagado do request HTTP até o consumo do evento (ver
  [ADR-005](005-observabilidade-e-correlation-id.md)).

## Critérios de revisão

Revisar se surgir um requisito de negócio que realmente exija que o registro de um
lançamento só seja confirmado ao comerciante depois que o saldo tiver sido atualizado
(consistência forte) — nesse caso a integração assíncrona deixaria de atender ao requisito e
uma orquestração diferente (ex.: saga síncrona com compensação) precisaria ser avaliada.
