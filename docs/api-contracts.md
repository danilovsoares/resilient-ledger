# Contratos de API

## Objetivo do documento

Documentar os endpoints expostos pelas duas APIs: propósito, headers, request, response,
códigos HTTP e convenção de erros. O Swagger/OpenAPI gerado em tempo de execução
(`/swagger` em ambiente Development, em ambas as APIs) é a fonte de contrato **executável**;
este documento é a referência de leitura rápida.

## Escopo

Ledger API (`http://localhost:5080` no ambiente local) e Daily Balance API
(`http://localhost:5081`). Todos os endpoints de negócio exigem `Authorization: Bearer <token>`
(ver [security.md](security.md) para como obter um token em desenvolvimento).

## Convenção de erros — Problem Details

Todas as respostas de erro seguem RFC 9457 (Problem Details), produzidas por
`GlobalExceptionHandler` ou pelo `ValidationProblem` nativo do ASP.NET Core:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Regra de negócio violada",
  "status": 400,
  "detail": "O valor do lançamento deve ser positivo.",
  "instance": "/api/v1/transactions"
}
```

Erros de validação de request (`FluentValidation`) retornam a variação com `errors` por campo:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Amount": ["O valor do lançamento deve ser positivo."]
  }
}
```

## Ledger API

### `POST /api/v1/auth/login`

Autentica com usuário/senha e emite o JWT Bearer usado nos demais endpoints (ver
[security.md](security.md)). Não requer autenticação prévia.

| | |
|---|---|
| Autenticação | Nenhuma |
| Rate limiting | Política dedicada e mais apertada que o restante da Api (10 tentativas/minuto por IP, configurável via `RateLimiting:LoginPermitLimit`) |

Request body:

```json
{
  "username": "comerciante",
  "password": "verity123"
}
```

Respostas:

| Código | Quando | Corpo |
|---|---|---|
| 200 OK | Credenciais válidas | `{ "accessToken": "...", "username": "...", "displayName": "..." }` |
| 400 Bad Request | `username`/`password` ausentes ou acima do limite (128/72 caracteres) | Problem Details com `errors` por campo |
| 401 Unauthorized | Usuário inexistente ou senha incorreta — resposta idêntica nos dois casos, para não revelar quais usuários existem | Problem Details genérico ("Credenciais inválidas") |
| 429 Too Many Requests | Limite de tentativas de login excedido | — |

### `POST /api/v1/transactions`

Registra um lançamento de crédito ou débito. Idempotente por `Idempotency-Key`.

| | |
|---|---|
| Autenticação | `Authorization: Bearer <token>` (obrigatório) |
| Headers obrigatórios | `Idempotency-Key: <string>` |
| Headers opcionais | `X-Correlation-ID: <guid>` — gerado pelo servidor se ausente |

Request body:

```json
{
  "type": 1,
  "amount": 150.50,
  "occurredAt": "2026-09-02T10:00:00Z",
  "description": "Venda balcão"
}
```

- `type`: `1` = Crédito, `2` = Débito.
- `amount`: decimal, obrigatoriamente positivo.
- `occurredAt`: opcional; se omitido, usa o instante do servidor (UTC).
- `description`: opcional, até 500 caracteres.

Respostas:

| Código | Quando | Corpo |
|---|---|---|
| 201 Created | Novo lançamento registrado | O lançamento criado (`TransactionDto`) |
| 200 OK | `Idempotency-Key` já usada anteriormente (replay idempotente) | O lançamento original, sem duplicar |
| 400 Bad Request | Validação falhou (valor não positivo, `Idempotency-Key` ausente, etc.) | Problem Details |
| 401 Unauthorized | Token ausente ou inválido | — |
| 429 Too Many Requests | Limite de taxa excedido | — |

Corpo de resposta (`TransactionDto`):

```json
{
  "id": "f7ee4c86-9641-444e-96c6-1c53011edca6",
  "type": 1,
  "amount": 150.50,
  "occurredAt": "2026-09-02T10:00:00+00:00",
  "businessDate": "2026-09-02",
  "description": "Venda balcão",
  "idempotencyKey": "chave-do-cliente-001",
  "createdAt": "2026-09-02T16:23:24.34Z"
}
```

### `POST /api/v1/transactions/{id}/cancel`

Estorna um lançamento: registra um **novo** lançamento, de tipo oposto e mesmo valor, que zera
o efeito do original no saldo. O lançamento original nunca é alterado ou removido — não existe
edição de lançamentos neste domínio (ver [ADR-004](adr/004-consistencia-eventual-e-ordenacao.md)).

| | |
|---|---|
| Autenticação | `Authorization: Bearer <token>` (obrigatório) |
| Path param | `id` (guid do lançamento a estornar) |

Respostas:

| Código | Quando | Corpo |
|---|---|---|
| 200 OK | Estorno registrado | O lançamento de estorno criado (`TransactionDto`, com `reversalOfTransactionId` apontando para o original) |
| 400 Bad Request | O lançamento já havia sido estornado antes | Problem Details |
| 401 Unauthorized | Token ausente ou inválido | — |
| 404 Not Found | Nenhum lançamento com este `id` | — |
| 429 Too Many Requests | Limite de taxa excedido | — |

`TransactionDto` ganha dois campos além dos já documentados acima: `reversalOfTransactionId`
(preenchido quando o próprio lançamento é um estorno de outro) e `reversedByTransactionId`
(preenchido quando este lançamento já foi estornado por outro — calculado em toda consulta,
não armazenado como estado mutável).

### `GET /api/v1/transactions?date=yyyy-MM-dd&page=&pageSize=`

Consulta paginada dos lançamentos de uma data de negócio (UTC), ordenados por `occurredAt`.

| | |
|---|---|
| Autenticação | `Authorization: Bearer <token>` (obrigatório) |
| Query param | `date` (obrigatório, formato `yyyy-MM-dd`) |
| Query param | `page` (opcional, padrão `1`; valores `< 1` são normalizados para `1`) |
| Query param | `pageSize` (opcional, padrão `10`; limitado ao intervalo `[1, 10]` — o servidor nunca retorna mais de 10 itens por página, mesmo se o cliente pedir mais) |

Resposta 200 OK (`PagedResult<TransactionDto>`):

```json
{
  "items": [ /* TransactionDto[], pode ser vazio */ ],
  "page": 1,
  "pageSize": 10,
  "totalCount": 23,
  "totalPages": 3
}
```

## Daily Balance API

### `GET /api/v1/daily-balances/{date}`

Consulta o saldo consolidado de uma data de negócio (UTC).

| | |
|---|---|
| Autenticação | `Authorization: Bearer <token>` (obrigatório) |
| Path param | `date` (formato `yyyy-MM-dd`) |

Resposta 200 OK:

```json
{
  "businessDate": "2026-09-02",
  "totalCredits": 150.50,
  "totalDebits": 40.00,
  "balance": 110.50,
  "updatedAt": "2026-09-02T16:23:32.87Z"
}
```

Uma data sem nenhum lançamento consolidado retorna 200 OK com `totalCredits`/`totalDebits`/`balance`
zerados e `updatedAt: null` — **nunca 404**: a ausência de lançamentos é um estado de negócio
válido (dia sem movimento), não um erro.

## Health checks

Ambas as APIs (e o Worker, na sua porta própria) expõem, sem autenticação:

| Endpoint | Propósito |
|---|---|
| `GET /health/live` | Sempre 200 se o processo está de pé; nenhuma dependência é checada. Usado para decidir se o container deve ser reiniciado. |
| `GET /health/ready` | 200 apenas se as dependências críticas (PostgreSQL, e RabbitMQ/Redis conforme o serviço) estão acessíveis. Usado para decidir se o container deve receber tráfego. |

Ver [07 — Deployment](architecture/07-deployment-local-e-producao.md) para o detalhamento por
serviço.

## Swagger/OpenAPI como contrato executável

Ambas as APIs expõem Swagger UI (`/swagger`) quando `ASPNETCORE_ENVIRONMENT=Development` (é o
caso no `docker-compose.yml` local). O JSON OpenAPI gerado (`/swagger/v1/swagger.json`) reflete
exatamente os DTOs, validações anotadas e códigos de resposta declarados em cada controller —
é a fonte de verdade para integração automatizada (geração de client, testes de contrato), este
documento é a referência de leitura humana.

## Referências

- [Segurança](security.md)
- [Estratégia de testes](testing-strategy.md)
- [Requisitos não funcionais](non-functional-requirements.md)
