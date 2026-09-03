# E2E — Jornada do comerciante

Testes end-to-end (Playwright) que sobem a stack real via `docker-compose.yml` (frontend +
as duas APIs + Postgres/RabbitMQ/Redis) e navegam a jornada completa do comerciante: login,
registro de um lançamento e consulta do saldo diário consolidado. Não fazem mock de rede —
o objetivo é validar o fluxo ponta a ponta, incluindo a consistência eventual do saldo
(Outbox → RabbitMQ → Worker).

Rodam automaticamente no CI (job `e2e-tests` em `.github/workflows/ci.yml`), que também
publica as capturas de tela geradas em `screenshots/` e o relatório HTML do Playwright como
artifacts do workflow.

## Rodando localmente

```bash
# a partir da raiz do repositório
docker compose up -d --build

cd tests/e2e
npm ci
npx playwright install --with-deps chromium
npm test
```

Credenciais e URL base são configuráveis via variáveis de ambiente (`E2E_USERNAME`,
`E2E_PASSWORD`, `E2E_BASE_URL`) — os padrões já casam com o usuário seedado pelo
`docker-compose.yml` (`comerciante` / `verity123`) e com a porta exposta do serviço `web`
(`http://localhost:4201`).
