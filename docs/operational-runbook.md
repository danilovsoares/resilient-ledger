# Runbook Operacional

## Objetivo do documento

Procedimentos curtos e diretos para os cenários de falha mais prováveis em operação. Cada
procedimento segue o mesmo formato: sintoma, dados a verificar, ação segura, critério de
recuperação e registro posterior.

## Escopo

Operação do ambiente Docker Compose local ou de uma implantação equivalente. Comandos de
diagnóstico assumem os nomes de serviço do `docker-compose.yml` (`ledger-api`,
`daily-balance-api`, `daily-balance-worker`, `postgres`, `rabbitmq`, `redis`).

## 1. Broker indisponível e Outbox acumulada

- **Sintoma**: `verity.ledger.outbox.pending` crescendo continuamente (ver
  [observability.md](observability.md)); logs do `OutboxPublisherService` com avisos repetidos
  de falha de publicação.
- **Dados a verificar**: `docker compose ps rabbitmq` (container saudável?);
  `GET http://localhost:5080/health/ready` (o check `rabbitmq` reporta `Unhealthy`?);
  `SELECT count(*) FROM outbox_messages WHERE published_at IS NULL;` no banco `verity_ledger`.
- **Ação segura**: restaurar o RabbitMQ (`docker compose restart rabbitmq` ou investigar a causa
  raiz de infraestrutura). Não é necessária nenhuma ação manual sobre `outbox_messages` — o
  `OutboxPublisherService` drena o backlog automaticamente assim que o broker volta.
- **Critério de recuperação**: `outbox_messages` pendentes voltando a zero e `/health/ready` do
  Ledger reportando saudável.
- **Registro posterior**: anotar o tempo total de indisponibilidade do broker e o pico de
  mensagens pendentes observado, para dimensionar alertas futuros.

## 2. Consumidor (Daily Balance Worker) parado

- **Sintoma**: saldo consolidado parou de refletir novos lançamentos; fila de consumo do
  RabbitMQ crescendo sem consumidores ativos (visível na management UI,
  `http://localhost:15673`).
- **Dados a verificar**: `docker compose ps daily-balance-worker`;
  `GET http://localhost:5082/health/ready`; logs do worker (`docker compose logs
  daily-balance-worker --tail=200`).
- **Ação segura**: `docker compose restart daily-balance-worker`. O consumo é idempotente
  (Inbox) — reiniciar o worker nunca duplica saldo, mesmo que uma mensagem estivesse "em voo"
  no momento da parada.
- **Critério de recuperação**: fila de consumo voltando a esvaziar na management UI e
  `daily_balances.updated_at` avançando para novos lançamentos.
- **Registro posterior**: causa raiz da parada (OOM, exceção não tratada, deploy) e tempo até a
  detecção.

## 3. Mensagens em DLQ

- **Sintoma**: fila `*_error` do RabbitMQ (criada automaticamente pelo MassTransit para o
  receive endpoint do consumidor) com profundidade maior que zero.
- **Dados a verificar**: inspecionar a mensagem na management UI — corpo, headers de falha
  (`MT-Fault-Message`, `MT-Fault-StackTrace`) e o `CorrelationId` propagado.
- **Ação segura**: identificar e corrigir a causa raiz (ex.: bug no handler, dado malformado).
  Só então mover a mensagem de volta para a fila de consumo original (via management UI ou uma
  ferramenta de shovel). Isso é seguro — o `EventId` daquela mensagem nunca chegou a ser gravado
  em `processed_messages` (ela falhou antes de completar a transação), então reprocessá-la
  aplica o efeito pela primeira vez, sem duplicar saldo.
- **Critério de recuperação**: fila de erro vazia e o saldo da data afetada refletindo o
  lançamento reprocessado.
- **Registro posterior**: causa raiz documentada; se foi um bug de código, referenciar o fix.

## 4. Cache Redis indisponível

- **Sintoma**: `GET http://localhost:5081/health/ready` reporta o check `redis` como
  `Degraded`; latência da consulta de saldo aumentando (mais chamadas caindo direto no
  PostgreSQL).
- **Dados a verificar**: `docker compose ps redis`; `verity.dailybalance.cache.misses` crescendo
  desproporcionalmente em relação a `verity.dailybalance.cache.hits` (ver
  [observability.md](observability.md)).
- **Ação segura**: `docker compose restart redis`. Nenhuma ação é necessária na Daily Balance
  API — `RedisDailyBalanceCache` já trata falhas de conexão como cache miss silencioso e cai
  para o PostgreSQL automaticamente; a consulta continua funcional durante a indisponibilidade.
- **Critério de recuperação**: health check `redis` voltando a `Healthy` e a razão de cache hit
  normalizando.
- **Registro posterior**: tempo de indisponibilidade do Redis e impacto observado na latência
  p95 da consulta.

## 5. Aumento de latência/erros no endpoint de consulta

- **Sintoma**: p95 de `GET /api/v1/daily-balances/{date}` acima da meta (ver
  [performance-and-capacity.md](performance-and-capacity.md)) ou aumento de respostas 5xx.
- **Dados a verificar**: `verity.dailybalance.cache.hits`/`.misses` (queda na taxa de acerto?);
  `/health/ready` da Daily Balance API (PostgreSQL saudável?); logs estruturados filtrados por
  `Service=verity-daily-balance-api` e status 5xx.
- **Ação segura**: se o Redis estiver indisponível, seguir o procedimento 4. Se o PostgreSQL
  estiver com latência alta, verificar conexões ativas e locks
  (`SELECT * FROM pg_stat_activity;`). Escalar réplicas da Daily Balance API é seguro a
  qualquer momento — é um serviço stateless.
- **Critério de recuperação**: p95 voltando abaixo da meta e taxa de erro 5xx normalizando.
- **Registro posterior**: se a causa foi volume de tráfego, considerar ajustar o rate limit ou o
  número de réplicas.

## 6. Rastrear uma operação pelo CorrelationId

- **Sintoma**: necessidade de investigar "o que aconteceu com o lançamento X" (suporte,
  auditoria, depuração).
- **Procedimento**:
  1. Obter o `CorrelationId` — do header `X-Correlation-ID` da resposta original, ou consultando
     `SELECT correlation_id FROM transactions t JOIN outbox_messages o ON ... WHERE t.id = ?`
     (join lógico via payload) no banco do Ledger.
  2. Filtrar os logs estruturados de ambos os serviços por esse `CorrelationId` (ver
     [observability.md](observability.md)) — reconstrói a jornada do request HTTP até o
     processamento do evento.
  3. Confirmar a aplicação do efeito consultando
     `SELECT * FROM processed_messages WHERE correlation_id = ?` no banco do Daily Balance.
- **Critério de conclusão**: jornada completa reconstruída (request → outbox → publicação →
  consumo → atualização de saldo) ou identificado o ponto exato em que ela parou.
- **Registro posterior**: nenhum, salvo se a investigação revelar um bug — nesse caso, seguir o
  procedimento correspondente acima.

## Referências

- [Resiliência e mensageria](resiliency-and-messaging.md)
- [Observabilidade](observability.md)
- [Requisitos não funcionais](non-functional-requirements.md)
