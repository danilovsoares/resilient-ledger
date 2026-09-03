# 07 — Deployment Local e Produção

## Objetivo

Descrever como a solução é executada localmente via Docker Compose e como ela evoluiria para
um ambiente de produção em Azure.

## Escopo

Topologia de execução. Para os containers em si, ver
[03-visao-de-containers-c4.md](03-visao-de-containers-c4.md). A seção de produção descreve
uma **evolução recomendada, não implementada** — ver [future-evolution.md](../future-evolution.md).

## Ambiente local (Docker Compose)

`docker-compose.yml`, na raiz do repositório, sobe sete serviços:

| Serviço | Imagem/build | Porta no host |
|---|---|---|
| `postgres` | `postgres:16-alpine` | 5433 |
| `rabbitmq` | `rabbitmq:3-management-alpine` | 5673 (AMQP), 15673 (management UI) |
| `redis` | `redis:7-alpine` | 6380 |
| `ledger-api` | build de `src/Ledger/Verity.Ledger.Api/Dockerfile` | 5080 |
| `daily-balance-api` | build de `src/DailyBalance/Verity.DailyBalance.Api/Dockerfile` | 5081 |
| `daily-balance-worker` | build de `src/DailyBalance/Verity.DailyBalance.Worker/Dockerfile` | 5082 (apenas health checks) |
| `web` | build de `frontend/verity-web/Dockerfile` (Angular + nginx) | 4201 |

O PostgreSQL sobe com um único container, mas dois bancos (`verity_ledger`,
`verity_daily_balance`), criados pelo script `deploy/postgres/init-databases.sh` montado em
`/docker-entrypoint-initdb.d`. Os três serviços .NET usam variáveis de ambiente para
sobrescrever `appsettings.json` (convenção `Seção__Chave` do ASP.NET Core), incluindo
`ConnectionStrings__LedgerDb` / `ConnectionStrings__DailyBalanceDb`, `RabbitMq__Host`,
`Redis__ConnectionString` e `Jwt__SigningKey`.

`docker-compose.yml` define `healthcheck` para todos os serviços com dependências
(`depends_on: condition: service_healthy`), garantindo que as APIs só iniciem depois que
PostgreSQL/RabbitMQ/Redis já estejam saudáveis.

### Subindo o ambiente

```bash
docker compose up -d --build
```

Com `ASPNETCORE_ENVIRONMENT=Development` (definido no compose), as três aplicações .NET:

- aplicam automaticamente as migrations do EF Core na inicialização
  (`Database:AutoMigrate=true`);
- expõem Swagger UI (`/swagger`) nas duas APIs;
- expõem `POST /api/v1/dev/token` na Ledger API, para obter um token JWT de teste sem um
  identity provider real (ver [security.md](../security.md)).

## Variáveis de ambiente e configuração por ambiente

Cada serviço .NET segue o padrão padrão do ASP.NET Core: `appsettings.json` (valores default,
seguros para produção — sem segredos), `appsettings.Development.json` (sobrescreve com
valores convenientes para desenvolvimento local, incluindo a chave de assinatura JWT de
desenvolvimento) e variáveis de ambiente (usadas pelo Docker Compose para apontar cada
serviço para os hosts corretos dentro da rede Docker). Em produção, a chave de assinatura JWT
e as credenciais de banco/broker nunca vêm de arquivo — ver [security.md](../security.md).

## Health checks

Todos os três serviços .NET expõem dois endpoints, sem autenticação:

- **`/health/live`**: não executa nenhuma verificação (`Predicate = _ => false`) — só indica
  que o processo está de pé e respondendo. Usado por orquestradores para decidir se devem
  reiniciar o container.
- **`/health/ready`**: executa as verificações marcadas com a tag `ready` — PostgreSQL
  (`AddNpgSql`) em todos os três; RabbitMQ (checagem própria, abrindo e fechando uma conexão
  de curta duração) na Ledger API e no Worker; Redis (`PingAsync`) na Daily Balance API,
  reportando `Degraded` (não `Unhealthy`) quando o Redis falha, porque a consulta continua
  funcional via fallback ao PostgreSQL. Usado por orquestradores para decidir se o container
  deve receber tráfego.

## Produção — evolução em Azure

O desafio não pede implantação real em nuvem; esta seção descreve a evolução pretendida,
mantendo os mesmos limites de contexto e os mesmos padrões de resiliência já implementados:

| Componente local | Equivalente em Azure |
|---|---|
| Docker Compose | Azure Container Apps (mais simples) ou AKS (mais controle), um deployment por serviço |
| RabbitMQ | Azure Service Bus (filas/tópicos gerenciados) |
| Redis | Azure Cache for Redis |
| PostgreSQL | Azure Database for PostgreSQL Flexible Server, com alta disponibilidade (par primário/réplica) — uma instância por contexto, eliminando o compartilhamento hoje usado localmente |
| Variáveis de ambiente com segredos | Azure Key Vault, referenciado via managed identity |
| Observabilidade | Application Insights / Azure Monitor como backend do OpenTelemetry (via exportador OTLP) |
| Exposição pública | Azure API Management (contrato, throttling adicional) atrás de um Application Gateway/WAF |
| Registro de imagens | Azure Container Registry (ACR) |
| CI/CD | Azure DevOps ou GitHub Actions (o workflow em `.github/workflows/ci.yml` já cobre build e testes; o deploy para Azure é a extensão natural) |

### Autoscaling

- **Ledger API / Daily Balance API**: autoscaling por CPU/memória (métrica padrão de
  Container Apps/AKS HPA), pois são cargas de request-response.
- **Daily Balance Worker**: candidato natural a autoscaling por **profundidade de fila**
  (quantidade de mensagens pendentes no RabbitMQ/Service Bus), não por CPU — um worker
  orientado a fila escala melhor em função do backlog do que da utilização de CPU, que pode
  ficar baixa mesmo com fila crescendo (ex.: enquanto aguarda I/O de banco). **KEDA** é a
  opção natural para isso em Kubernetes/AKS, com um scaler de RabbitMQ/Service Bus observando
  o tamanho da fila diretamente.

## Referências

- [03 — Visão de Containers](03-visao-de-containers-c4.md)
- [Segurança](../security.md)
- [Observabilidade](../observability.md)
- [Evolução futura](../future-evolution.md)
