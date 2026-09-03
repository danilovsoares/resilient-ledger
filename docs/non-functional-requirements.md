# Requisitos Não Funcionais

## Objetivo

Tornar mensurável o requisito não funcional determinante do desafio, distinguindo
explicitamente **meta** (SLO — o que nos comprometemos a buscar), **indicador** (SLI — o que
medimos) e **resultado observado** (o que foi de fato verificado neste ambiente local). Este
documento não afirma disponibilidade absoluta nem perda zero de eventos — essas garantias não
existem em nenhum sistema distribuído real, e afirmá-las seria falso.

## Escopo

Requisitos não funcionais do sistema como um todo. Para o detalhamento de mensageria e
falhas, ver [resiliency-and-messaging.md](resiliency-and-messaging.md); para a metodologia de
carga, ver [performance-and-capacity.md](performance-and-capacity.md).

## Matriz

| Categoria | Meta (SLO) | Estratégia de implementação | Evidência |
|---|---|---|---|
| Disponibilidade do Ledger | Não depender do consolidado | Outbox + mensageria assíncrona, sem chamada síncrona ao Daily Balance | Teste manual e de integração com o Daily Balance/RabbitMQ indisponíveis (ver abaixo) |
| Disponibilidade de consulta | 99,5% como meta proposta (SLO interno, não medido em produção real) | Cache Redis + índice em `business_date`/`business_date` PK + health checks | Sem SLI de produção (não há produção); validado localmente via k6 e health checks |
| Erro sob carga | Menor que 5% a 50 RPS | Cache-aside, consulta por chave primária, rate limiting configurado acima do alvo | Execução real de k6 local: **0% de erro**, p95 = 2,05ms, a 50 RPS sustentados por 2 minutos (ver [performance-and-capacity.md](performance-and-capacity.md)) |
| Integridade | Sem duplicar efeito de evento | Inbox (`processed_messages`) com `EventId` como chave primária, checada antes de aplicar qualquer mudança | Validado nos testes de integração de reentrega (ver [testing-strategy.md](testing-strategy.md)) |
| Recuperação | Nenhum evento confirmado é perdido entre banco e mensageria | Outbox — lançamento e evento gravados na mesma transação | Validado nos testes de integração de persistência + Outbox |
| Observabilidade | Rastreabilidade ponta a ponta | `CorrelationId` propagado do request HTTP até `processed_messages`; logs estruturados Serilog | Validado manualmente (header de resposta, coluna `correlation_id` nas duas tabelas) |
| Segurança | API protegida e entradas validadas | JWT Bearer, rate limiting, FluentValidation, Problem Details sem detalhes internos | Validado manualmente (endpoint sem token retorna 401; payload inválido retorna 400) |

## Detalhamento por item

### Disponibilidade do Ledger

O Ledger não faz nenhuma chamada HTTP nem consulta ao banco do Daily Balance. Isso foi
validado manualmente subindo apenas `postgres`, `rabbitmq` e `ledger-api` (sem
`daily-balance-api`/`daily-balance-worker`) e confirmando que `POST /api/v1/transactions`
continua respondendo 201 normalmente. O mesmo vale para uma indisponibilidade do próprio
RabbitMQ: o lançamento é persistido e a Outbox absorve a intenção de publicação — ver
[resiliency-and-messaging.md](resiliency-and-messaging.md).

### Erro sob carga (50 RPS)

A meta do desafio foi validada com uma execução real de k6 (não simulada), descrita em
detalhe em [performance-and-capacity.md](performance-and-capacity.md) e cujo resultado bruto
está salvo em `k6/resultados/execucao-local-2026-09-02.txt`: 0% de taxa de erro e p95 de
2,05ms de latência, ambos dentro dos thresholds definidos no próprio script de carga
(`http_req_failed: rate<0.05`, `http_req_duration: p(95)<300`). **Este é um resultado de
notebook local**, com todos os componentes rodando na mesma máquina — não é uma garantia de
comportamento sob condições de rede e hardware de produção; serve como evidência
reprodutível de que o desenho (cache-aside + projeção pré-agregada) atinge a meta neste
ambiente.

### Integridade e recuperação

Cobertos estruturalmente pelo padrão Outbox/Inbox (ver
[ADR-003](adr/003-transactional-outbox-e-inbox.md)) e verificados por testes de integração
que inspecionam diretamente as tabelas envolvidas (ver [testing-strategy.md](testing-strategy.md)).

### Disponibilidade de consulta (99,5%)

Este número é uma **meta proposta**, não uma medição de produção — não existe ambiente de
produção real para esta solução. Não a apresentamos como um compromisso cumprido, apenas como
o SLO que orientaria o design de alertas e runbooks caso a solução fosse para produção (ver
[operational-runbook.md](operational-runbook.md)).

## Referências

- [Mensageria e resiliência](resiliency-and-messaging.md)
- [Observabilidade](observability.md)
- [Performance e capacidade](performance-and-capacity.md)
- [Estratégia de testes](testing-strategy.md)
