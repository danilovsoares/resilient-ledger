# Fluxo de Caixa (Ledger + Daily Balance)

[![CI](https://github.com/danilovsoares/resilient-ledger/actions/workflows/ci.yml/badge.svg)](https://github.com/danilovsoares/resilient-ledger/actions/workflows/ci.yml)

Solução para o desafio de arquiteto de software: um comerciante registra lançamentos diários de
crédito/débito e consulta o saldo consolidado por dia. O requisito central é que **o registro de
lançamentos nunca fique indisponível se o serviço de consolidado cair**, e que a consulta de
saldo suporte 50 requisições por segundo com no máximo 5% de erro.

A documentação arquitetural completa está em [`docs/`](docs/) — comece por
[`docs/architecture/01-contexto-e-objetivos.md`](docs/architecture/01-contexto-e-objetivos.md).

## Arquitetura em uma frase

Dois serviços independentes — **Ledger** (escrita) e **Daily Balance** (leitura) — integrados de
forma assíncrona via RabbitMQ, com Transactional Outbox no produtor e Inbox idempotente no
consumidor, para que a indisponibilidade de um nunca afete o outro. Detalhes e trade-offs em
[`docs/adr/`](docs/adr/).

## Stack

| Área | Tecnologia |
|---|---|
| Backend | .NET 10, ASP.NET Core, C# |
| Frontend | Angular 22 (standalone + signals) |
| Dados | PostgreSQL (bancos separados por contexto) |
| Integração | RabbitMQ + MassTransit |
| Cache | Redis (cache-aside) |
| Logs | Serilog (JSON estruturado) |
| Telemetria | OpenTelemetry (traces + métricas) |
| Testes | xUnit, FluentAssertions, Testcontainers |
| Carga | k6 |
| Execução | Docker Compose |
| CI | GitHub Actions |

## Estrutura do repositório

```text
src/
├── Shared/Verity.Shared.Contracts        # Contrato de integração publicado no broker
├── Ledger/                               # Domain, Application, Infrastructure, Api
└── DailyBalance/                         # Domain, Application, Infrastructure, Api, Worker
tests/                                    # xUnit: unitários e integração (Testcontainers)
tests/e2e/                                # Playwright: 11 cenários contra a stack real
frontend/verity-web/                      # Angular 22 (testes unitários em src/app/**/*.spec.ts)
k6/                                       # Script e resultados de carga
docs/                                     # Documentação arquitetural completa
deploy/postgres/                          # Script de inicialização dos bancos locais
docker-compose.yml
.github/workflows/ci.yml
```

## Pré-requisitos

- Docker e Docker Compose
- .NET 10 SDK (para rodar testes/builds fora do Docker)
- Node.js 22+ (para rodar o frontend fora do Docker)

## Executando localmente

```bash
docker compose up -d --build
```

Isso sobe: PostgreSQL (bancos `verity_ledger` e `verity_daily_balance`), RabbitMQ, Redis, as
duas APIs, o Daily Balance Worker e o frontend Angular.

| Serviço | URL |
|---|---|
| Frontend | http://localhost:4201 |
| Ledger API (Swagger) | http://localhost:5080/swagger |
| Daily Balance API (Swagger) | http://localhost:5081/swagger |
| RabbitMQ management | http://localhost:15673 (guest/guest) |

As migrations do EF Core são aplicadas automaticamente na subida (`Database:AutoMigrate=true`
no ambiente Development do Compose).

### Login

O frontend tem uma tela de login real: as credenciais são validadas contra um usuário
persistido no PostgreSQL da Ledger API (senha com hash `BCrypt`), não apenas um token emitido
sem checagem. Autenticação corporativa completa (cadastro, múltiplos perfis, SSO) está fora do
escopo do desafio (ver [`docs/security.md`](docs/security.md)) — por isso não há tela de
cadastro: o único usuário é provisionado automaticamente na subida do Compose com a credencial:

| Usuário | Senha |
|---|---|
| `comerciante` | `verity123` |

### Testando a API manualmente

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"comerciante","password":"verity123"}' \
  | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')

# Registrar um lançamento
curl -s -i -X POST http://localhost:5080/api/v1/transactions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: exemplo-001" \
  -d '{"type":1,"amount":150.50,"occurredAt":"2026-09-02T10:00:00Z","description":"Venda balcao"}'

# Consultar o saldo consolidado do dia (após alguns segundos, consistência eventual)
curl -s "http://localhost:5081/api/v1/daily-balances/2026-09-02" \
  -H "Authorization: Bearer $TOKEN"
```

## Rodando os testes

```bash
dotnet test Verity.slnx
```

Os testes de integração usam Testcontainers (PostgreSQL/RabbitMQ efêmeros via Docker) — não é
necessário subir o `docker-compose.yml` antes, apenas ter o Docker em execução.

Testes unitários do frontend (Vitest):

```bash
cd frontend/verity-web
npm ci
npm test
```

Testes E2E (Playwright) — navegam a aplicação real contra a stack completa, com screenshot e
vídeo de cada cenário:

```bash
docker compose up -d --build
cd tests/e2e
npm ci
npx playwright install --with-deps chromium
npm test
```

Detalhes da estratégia de testes em [`docs/testing-strategy.md`](docs/testing-strategy.md) e
[`tests/e2e/README.md`](tests/e2e/README.md).

## Rodando o teste de carga (k6)

```bash
docker compose up -d
TOKEN=$(curl -s -X POST http://localhost:5080/api/v1/dev/token | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')

MSYS_NO_PATHCONV=1 docker run --rm --network verity_default \
  -v "$(pwd)/k6:/scripts" \
  -e BASE_URL=http://daily-balance-api:8080 \
  -e TOKEN="$TOKEN" \
  -e BUSINESS_DATE=$(date -u +%Y-%m-%d) \
  grafana/k6:latest run /scripts/consolidado-50rps.js
```

Metodologia e resultado de referência em
[`docs/performance-and-capacity.md`](docs/performance-and-capacity.md).

## Documentação

| Documento | Conteúdo |
|---|---|
| [`docs/architecture/`](docs/architecture/) | Contexto, C4 (contexto/containers), componentes e camadas, fluxos principais, modelo de dados, deployment |
| [`docs/adr/`](docs/adr/) | 7 decisões de arquitetura com alternativas e trade-offs |
| [`docs/non-functional-requirements.md`](docs/non-functional-requirements.md) | Metas não funcionais, como são implementadas e verificadas |
| [`docs/resiliency-and-messaging.md`](docs/resiliency-and-messaging.md) | Garantias de entrega, Outbox/Inbox, retry, DLQ |
| [`docs/observability.md`](docs/observability.md) | Logs, métricas, tracing, CorrelationId |
| [`docs/security.md`](docs/security.md) | Autenticação, rate limiting, segredos |
| [`docs/api-contracts.md`](docs/api-contracts.md) | Endpoints, contratos, códigos HTTP |
| [`docs/testing-strategy.md`](docs/testing-strategy.md) | Pirâmide de testes e cenários cobertos |
| [`docs/performance-and-capacity.md`](docs/performance-and-capacity.md) | Metodologia e resultado do teste de carga |
| [`docs/operational-runbook.md`](docs/operational-runbook.md) | Procedimentos para os cenários de falha mais prováveis |
| [`docs/future-evolution.md`](docs/future-evolution.md) | O que ficou fora do escopo e por quê |

## Licença

Uso exclusivo para fins de avaliação do desafio técnico.
