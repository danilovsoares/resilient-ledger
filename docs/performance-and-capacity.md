# Performance e Capacidade

## Objetivo do documento

Definir a metodologia de carga usada para validar a meta de 50 RPS do desafio, e apresentar o
resultado obtido localmente — deixando claro o que isso prova e o que não prova.

## Escopo

`GET /api/v1/daily-balances/{date}` (Daily Balance API), o endpoint com meta de carga explícita
no desafio.

## Metodologia

Script k6 em `k6/consolidado-50rps.js`, com dois estágios:

1. **Aquecimento**: 10 RPS constantes por 30 segundos, para popular o cache Redis e estabilizar
   conexões (pool do Npgsql, conexões do k6) antes da medição.
2. **Carga constante**: 50 RPS constantes (`executor: constant-arrival-rate`) por 2 minutos,
   contra a mesma data de negócio usada no aquecimento — o cenário representativo é consulta
   repetida ao saldo do dia corrente, o padrão de acesso esperado de um comerciante consultando
   seu caixa.

Thresholds definidos no próprio script (o teste falha se não forem atingidos):

```js
thresholds: {
  http_req_failed: ["rate<0.05"],   // meta do desafio: erro < 5% a 50 RPS
  http_req_duration: ["p(95)<300"], // meta de latência adotada para a consulta
}
```

### Seed de dados

O cenário assume que já existe ao menos um lançamento consolidado para a data consultada
(criado manualmente ou via um lançamento de teste antes da execução) — sem isso, o teste ainda
passa (o endpoint retorna saldo zerado normalmente), mas não exercita a leitura de um registro
real em `daily_balances`.

## Como executar

```bash
docker compose up -d
# obter um token de desenvolvimento (ver README.md)
TOKEN=$(curl -s -X POST http://localhost:5080/api/v1/dev/token | ...)

docker run --rm --network verity_default \
  -v "$(pwd)/k6:/scripts" \
  -e BASE_URL=http://daily-balance-api:8080 \
  -e TOKEN="$TOKEN" \
  -e BUSINESS_DATE=2026-09-02 \
  grafana/k6:latest run /scripts/consolidado-50rps.js
```

(No Windows/Git Bash, prefixe com `MSYS_NO_PATHCONV=1` para evitar a conversão automática de
caminhos POSIX do MSYS.)

## Resultado de referência (execução local real)

Execução registrada em `k6/resultados/execucao-local-2026-09-02.txt`, contra a stack completa
rodando via `docker compose` na máquina do autor (API, Worker, PostgreSQL, RabbitMQ e Redis, sem
isolamento de rede/latência de produção):

| Indicador | Resultado | Threshold | Passou? |
|---|---|---|---|
| Taxa de erro | 0,00% | `< 5%` | Sim |
| Latência p95 | 2,05ms | `< 300ms` | Sim |
| Latência média | 1,48ms | — | — |
| Requisições totais | 6302 (~50/s no estágio de carga constante) | — | — |
| Checks bem-sucedidos | 100% (12604/12604) | — | — |

## O que este resultado prova — e o que não prova

**Prova**: que o desenho de leitura (projeção pré-agregada por chave primária + cache-aside
Redis) atende, com folga, à meta de 50 RPS com menos de 5% de erro **neste ambiente
controlado**. A margem observada (p95 de ~2ms contra um threshold de 300ms) sugere que o
gargalo real, se houver, estaria em outro lugar (rede de produção, contenção de recursos
compartilhados, autenticação/gateway na frente da API) — não na consulta em si.

**Não prova**: comportamento sob carga de produção real — rede real, múltiplos consumidores
concorrentes de fato distintos, contenção de CPU/IO compartilhada com outros workloads, ou
volume de dados ordens de magnitude maior (aqui o teste roda sobre uma tabela `daily_balances`
pequena). Resultados de notebook local são evidência reprodutível de que a arquitetura atende à
meta sob a carga alvo neste ambiente — não uma garantia de comportamento em produção (ver
[non-functional-requirements.md](non-functional-requirements.md), seção "O que não é
afirmado").

## Referências

- [Requisitos não funcionais](non-functional-requirements.md)
- [ADR-006 — Cache e estratégia de leitura](adr/006-cache-e-estrategia-de-leitura.md)
- [Estratégia de testes](testing-strategy.md)
