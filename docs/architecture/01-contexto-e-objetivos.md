# 01 — Contexto e Objetivos

## Objetivo

Este documento descreve o problema de negócio que esta solução resolve, os requisitos
funcionais e não funcionais adotados, as premissas assumidas para o escopo inicial e o que
foi deliberadamente deixado de fora. Ele é o ponto de partida para os demais documentos de
arquitetura em `docs/architecture/`.

## Escopo

Cobre a visão de negócio e os requisitos do sistema como um todo. Não descreve componentes
técnicos (ver [02-visao-de-contexto-c4.md](02-visao-de-contexto-c4.md) e
[03-visao-de-containers-c4.md](03-visao-de-containers-c4.md)) nem decisões de arquitetura
com alternativas (ver `docs/adr/`).

## Problema de negócio

Um comerciante precisa registrar, ao longo do dia, lançamentos financeiros de dois tipos —
crédito e débito — e consultar o saldo consolidado de um dia específico. A operação de
registro é o caminho crítico do negócio: se ela parar, o comerciante para de vender. A
consulta de saldo é importante, mas sua indisponibilidade momentânea não impede a operação
do caixa.

### Atores

- **Comerciante**: usuário final que registra lançamentos e consulta o saldo diário.
- **Aplicação Web (Angular)**: interface usada pelo comerciante; consome as duas APIs HTTP.
- **Ledger API**: recebe e persiste lançamentos.
- **Daily Balance API**: expõe a consulta do saldo diário consolidado.

O comerciante interage apenas com a aplicação Web. Ele não tem conhecimento de RabbitMQ,
Outbox, Inbox ou de que existem dois serviços por trás da experiência — essa separação é uma
decisão de arquitetura interna (ver [ADR-001](../adr/001-separacao-de-contextos-ledger-e-daily-balance.md)).

## Requisitos funcionais

1. Registrar um lançamento de crédito ou débito, com valor, data/hora de ocorrência e
   descrição opcional.
2. Consultar os lançamentos de uma data de negócio.
3. Consultar o saldo consolidado (total de créditos, total de débitos, saldo) de uma data de
   negócio.
4. Estornar um lançamento já registrado — via um novo lançamento reverso (tipo oposto, mesmo
   valor), nunca por edição do original (ver "Premissas" abaixo e
   [ADR-004](../adr/004-consistencia-eventual-e-ordenacao.md)).

## Requisitos não funcionais e metas adotadas

O requisito não funcional determinante do desafio, e que molda toda a arquitetura da solução, é:

> O serviço de lançamentos (Ledger) não pode ficar indisponível se o consolidado diário
> (Daily Balance) cair. O consolidado deve suportar 50 requisições por segundo, com no
> máximo 5% de perda de requisições.

A partir dele adotamos as metas detalhadas em
[non-functional-requirements.md](../non-functional-requirements.md), que distingue
explicitamente meta (SLO), indicador medido (SLI) e resultado observado localmente — sem
prometer disponibilidade absoluta ou zero perda de eventos.

## Premissas

- Valores de lançamento são sempre positivos; o sinal do efeito no saldo é dado pelo tipo
  (crédito soma, débito subtrai) — ver `Transaction.Register` em
  `Verity.Ledger.Domain`.
- A data de negócio (`BusinessDate`) é derivada do instante de ocorrência do lançamento
  convertido para UTC, não do fuso horário do cliente.
- Lançamentos são **imutáveis e aditivos**: um lançamento, uma vez registrado, nunca é alterado
  nem removido — nem mesmo o cancelamento (`POST /api/v1/transactions/{id}/cancel`) muda isso,
  já que ele registra um novo lançamento de estorno em vez de mutar o original. Isso é o que
  permite que o consolidado seja processado fora de ordem sem corromper o resultado (ver
  [ADR-004](../adr/004-consistencia-eventual-e-ordenacao.md)).
- A autenticação usada (JWT Bearer) tem um login real (`POST /api/v1/auth/login`), com
  credenciais validadas contra um usuário persistido — a simplificação de escopo é não haver
  cadastro nem múltiplos perfis: o único usuário é provisionado na subida. Para os testes de
  integração, a Ledger API também expõe `POST /api/v1/dev/token`, apenas em ambiente de
  desenvolvimento. Ver [security.md](../security.md) para o racional completo.

## Fora de escopo

- Conciliação bancária.
- Suporte a múltiplas moedas.
- Multiempresa / múltiplos comerciantes (multitenancy).
- Edição de lançamentos já registrados (cancelamento existe, via estorno — ver
  [future-evolution.md](../future-evolution.md)).
- Autenticação corporativa completa (identity provider, OAuth2/OIDC, múltiplos perfis de
  usuário).

Esses itens são detalhados, com o raciocínio de por que não entraram agora e o que mudaria
para suportá-los, em [future-evolution.md](../future-evolution.md).

## Glossário

| Termo | Significado |
|---|---|
| **Lançamento** (Transaction) | Registro de um evento financeiro do comerciante: um crédito ou um débito, com valor e data de ocorrência. |
| **Crédito** | Lançamento que aumenta o saldo do dia. |
| **Débito** | Lançamento que reduz o saldo do dia. |
| **Saldo Diário** (Daily Balance) | Projeção consolidada de créditos, débitos e saldo líquido de uma data de negócio. |
| **Outbox** | Tabela auxiliar no banco do Ledger onde o evento de integração é gravado na mesma transação do lançamento, garantindo que a escrita no banco e a intenção de publicar o evento sejam atômicas. |
| **Inbox** (`processed_messages`) | Tabela auxiliar no banco do Daily Balance que registra o `EventId` de cada evento já aplicado à projeção, usada para deduplicar reentregas do broker. |
| **DLQ** (Dead Letter Queue) | Fila para onde o MassTransit encaminha uma mensagem após esgotar as tentativas de reprocessamento configuradas, para não bloquear o processamento das demais mensagens. |
| **CorrelationId** | Identificador que amarra todas as etapas de uma mesma jornada de negócio (do request HTTP até a atualização do saldo), usado para rastreabilidade ponta a ponta. |
| **CausationId** | Identificador da mensagem/ação imediatamente anterior que causou um evento — diferente do CorrelationId, que identifica a jornada inteira. |
| **Idempotência** | Propriedade de uma operação que, executada mais de uma vez com a mesma entrada, produz o mesmo efeito de uma única execução — usada tanto na Api (`Idempotency-Key`) quanto no consumo de eventos (Inbox). |

## Referências

- [02 — Visão de Contexto (C4)](02-visao-de-contexto-c4.md)
- [Requisitos não funcionais](../non-functional-requirements.md)
- [ADR-001 — Separação de contextos](../adr/001-separacao-de-contextos-ledger-e-daily-balance.md)
- [Evolução futura](../future-evolution.md)
