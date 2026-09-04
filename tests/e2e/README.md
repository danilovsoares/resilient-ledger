# E2E — cenários do comerciante

Testes end-to-end (Playwright) que sobem a stack real via `docker-compose.yml` (frontend +
as duas APIs + Postgres/RabbitMQ/Redis) e navegam a aplicação como o comerciante navegaria.
Não fazem mock de rede — o objetivo é validar cada cenário ponta a ponta, incluindo a
consistência eventual do saldo (Outbox → RabbitMQ → Worker).

## Cenários cobertos

- **`auth.spec.ts`** — login com credenciais inválidas (mensagem de erro), login válido,
  rota protegida bloqueada sem sessão (`authGuard`), logout encerra a sessão de verdade.
- **`transactions.spec.ts`** — formulário não permite valor zero, registra crédito, registra
  débito, estorna um lançamento (novo lançamento reverso), paginação quando há mais de 10
  lançamentos no dia.
- **`daily-balance.spec.ts`** — saldo reflete um lançamento recém-registrado (consistência
  eventual), trocar a data consultada mostra o total exato daquela data.

Cenários que precisam de dados isolados (paginação, saldo de uma data exata) semeiam
lançamentos direto pela API (`helpers.ts#seedTransactions`) em vez de repetir o formulário —
mantém o teste rápido e focado no que ele realmente verifica.

Rodam automaticamente no CI (job `e2e-tests` em `.github/workflows/ci.yml`), que também
publica as capturas de tela (`screenshots/`), os vídeos de cada teste e o relatório HTML do
Playwright como artifacts do workflow.

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
