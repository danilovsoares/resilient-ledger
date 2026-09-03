# ADR 005 - Observabilidade e Correlation ID
Status: Aceita
Data: 2026-09-02

## Contexto

Uma jornada de negócio nesta solução atravessa, no mínimo, dois processos (Ledger API e Daily
Balance Worker) e um broker de mensagens no meio do caminho. Sem um identificador comum
propagado por toda essa cadeia, diagnosticar "o que aconteceu com o lançamento X" exigiria
correlacionar logs manualmente por timestamp ou por inspeção do payload — inviável em produção
sob carga.

## Decisão

Propagar um `CorrelationId` (GUID) desde o request HTTP original até o consumo do evento
correspondente, junto com `CausationId`, `EventId`, `TraceId` e `SpanId`:

- `CorrelationIdMiddleware`, no Ledger API e na Daily Balance API, lê o header
  `X-Correlation-ID` do request; se ausente ou inválido, gera um novo GUID. O valor é publicado
  no contexto de log (Serilog `LogContext`) e devolvido no header de resposta.
- Ao publicar o evento de integração, o `CorrelationId` do request é gravado na coluna
  `outbox_messages.correlation_id` e propagado como metadado da mensagem MassTransit
  (`PublishContext.CorrelationId`).
- Ao consumir, o `TransactionRegisteredConsumer` extrai o `CorrelationId` do evento e o registra
  em `processed_messages.correlation_id`, além de propagá-lo para o contexto de log do
  consumidor.
- `CausationId` identifica a origem imediata do evento — no fluxo atual, como o evento nasce
  diretamente do request HTTP (não há uma cadeia de eventos causando outros eventos), seu valor
  é igual ao `CorrelationId` da requisição original.
- `TraceId`/`SpanId` vêm do OpenTelemetry (instrumentação automática de ASP.NET Core, HttpClient,
  Npgsql e MassTransit), complementando o `CorrelationId` de negócio com rastreamento técnico
  distribuído.

## Alternativas consideradas

- **Nenhuma correlação explícita, depender só de TraceId do OpenTelemetry**: o `TraceId`
  identifica uma árvore de spans técnicos, mas não é pensado para ser um identificador de
  negócio estável, legível e fácil de correlacionar manualmente em logs estruturados (ex.: "me
  dê todo log relacionado a este lançamento"). Descartada como única estratégia.
- **CorrelationId gerado apenas pelo cliente, obrigatório**: forçaria todo chamador da API a
  gerar o identificador, tornando a API menos tolerante e mais frágil a integrações mal
  implementadas. Descartada em favor de um modelo em que o servidor sempre garante um valor
  (gera se ausente).
- **Header + geração automática no servidor, com propagação end-to-end** (decisão adotada):
  funciona tanto para clientes que já propagam CorrelationId quanto para os que não propagam.

## Consequências positivas

- É possível responder "como rastrear uma requisição da API ao worker" (critério de aceite do
  desafio) filtrando os logs estruturados por um único `CorrelationId` — do `POST
  /api/v1/transactions` até o log de aplicação do saldo no Worker.
- `X-Correlation-ID` no header de resposta permite ao próprio cliente (ex.: a aplicação Angular)
  reportar o identificador em caso de suporte, sem precisar inspecionar logs.
- A separação `CorrelationId` (jornada) vs. `CausationId` (origem imediata) vs. `EventId`
  (identidade do evento) deixa claro, em cada log, o papel de cada identificador — sem
  sobrecarregar um único campo com significados diferentes.

## Consequências negativas e mitigações

- **Mais campos para instrumentar em cada log estruturado**: aumenta a verbosidade de cada
  entrada de log. Mitigação: os campos são padronizados e sempre presentes (ver
  [observability.md](../observability.md)), o que torna a análise mais fácil, não mais difícil,
  uma vez que a estrutura é consistente.
- **Confiança em um valor gerado pelo cliente** (`X-Correlation-ID`) sem validação de formato
  rígida além de `Guid.TryParse`: um cliente malicioso poderia enviar valores não-únicos.
  Mitigação: o `CorrelationId` é usado para observabilidade, não para controle de acesso ou
  deduplicação de negócio (isso é papel do `EventId`/Inbox) — o pior caso é uma jornada com
  rastreamento confuso, não uma falha de integridade.

## Critérios de revisão

Revisitar se a solução crescer para mais de dois hops assíncronos (uma cadeia real de eventos
causando outros eventos), quando `CausationId` deixaria de ser sempre igual ao `CorrelationId` e
passaria a exigir uma lógica explícita de encadeamento.
