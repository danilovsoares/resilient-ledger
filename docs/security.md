# Segurança

## Objetivo do documento

Descrever os controles de segurança implementados, o racional de cada um e as simplificações de
escopo deliberadas — sem afirmar cobertura que o código não sustenta.

## Escopo

Ledger API e Daily Balance API (superfície pública). O Daily Balance Worker não expõe endpoints
de negócio, apenas health checks técnicos, sem autenticação.

## Autenticação e autorização (JWT Bearer)

Todos os endpoints de negócio (`TransactionsController`, `DailyBalancesController`) exigem
`[Authorize]`. A validação (`AddJwtBearer`) checa emissor, audiência, assinatura (HMAC-SHA256
com chave simétrica de configuração, `Jwt:SigningKey`) e expiração do token.

**Autenticação corporativa completa (identity provider externo, SSO, múltiplos perfis/papéis de
usuário) está fora do escopo do desafio.** O enunciado (`desafio-arquiteto-software-jun25.pdf`)
pede "autenticação, autorização, criptografia e mecanismos de proteção contra ataques" como parte
dos objetivos gerais de segurança, mas ressalva explicitamente: *"não é necessário que todas
essas premissas sejam apresentadas na codificação, mas nas decisões e representações
arquiteturais do projeto"*. A lista de requisitos técnicos obrigatórios (cuja ausência descarta o
teste) também não inclui autenticação.

Dito isso, o fluxo de login **é** implementado de ponta a ponta, não apenas decidido em
documentação: `POST /api/v1/auth/login` (`AuthController`) recebe usuário/senha, valida contra um
`User` persistido no PostgreSQL da Ledger API (senha com hash `BCrypt`, nunca em texto plano —
ver `IPasswordHasher`/`BCryptPasswordHasher`) e, se válido, emite um JWT assinado
(`JwtTokenFactory`), o mesmo formato validado por `AddJwtBearer` nas duas Apis. Credenciais
inválidas retornam 401 genérico — a resposta não distingue "usuário não existe" de "senha
errada" (`LoginHandler`), para não vazar quais nomes de usuário existem. O endpoint tem uma
política de rate limiting própria e mais apertada que o restante da Api (`login-fixed-window`,
padrão 10 tentativas/minuto por IP) para dificultar força bruta.

O que fica de fora, por decisão de escopo, é a **administração** de identidade: não há tela de
cadastro, recuperação de senha ou múltiplos perfis — o domínio de negócio é um único comerciante
(ver [01-contexto-e-objetivos.md](architecture/01-contexto-e-objetivos.md)). O primeiro (e único)
usuário é provisionado automaticamente na subida, via `DefaultUserSeeder`, a partir de
`Auth:DefaultUser:Username`/`Password` — só roda se `Auth:SeedDefaultUser=true` e a tabela de
usuários estiver vazia, para nunca recriar/sobrescrever uma credencial já definida. Em produção,
essa flag ficaria desligada por padrão e o provisionamento seria manual ou via pipeline, nunca
uma credencial previsível semeada automaticamente.

Para os testes de integração (que não devem depender de um usuário previamente cadastrado no
banco) e para automação — o script de carga k6 usa este endpoint para obter um token sem depender
do usuário seedado (ver [performance-and-capacity.md](performance-and-capacity.md)) —, a Ledger
API mantém `POST /api/v1/dev/token`, que emite um token válido sem nenhuma verificação de
identidade. Nunca é usado pela aplicação Web (o frontend usa exclusivamente o login real). Duas
salvaguardas garantem que isso nunca vaze para produção:

1. O endpoint só é registrado (`app.MapDevTokenEndpoint()`) quando
   `app.Environment.IsDevelopment()` é verdadeiro — em qualquer outro ambiente, a rota
   simplesmente não existe.
2. Está marcado com `.ExcludeFromDescription()`, não aparecendo no contrato OpenAPI/Swagger
   público.

Tanto `/api/v1/auth/login` quanto `/api/v1/dev/token` assinam com a mesma chave configurada em
`Jwt:SigningKey` (`JwtTokenFactory`, reaproveitado pelos dois) — ambos os serviços compartilham a
mesma chave simétrica, o que é aceitável no escopo atual (mesmo domínio de confiança) mas seria
substituído por chaves assimétricas e um emissor único ao evoluir para um IdP real (ver
[ADR-007](adr/007-seguranca-e-exposicao-de-api.md)).

### Configuração de desenvolvimento

`appsettings.Development.json` define um `Jwt:SigningKey` de exemplo, claramente marcado como
"dev-only-signing-key-not-for-production-use". `appsettings.json` (base, usado em qualquer
ambiente que não sobrescreva) mantém `SigningKey` vazio propositalmente — um valor vazio faz a
validação de token falhar de forma segura (nunca aceita um token "por acidente" em produção por
falta de configuração).

## Validação de payload e tratamento padronizado de erros

- `RegisterTransactionValidator` (FluentValidation) valida o comando antes de qualquer
  persistência: valor positivo, `Idempotency-Key` não vazia, tipo de lançamento válido,
  descrição limitada a 500 caracteres. Falhas de validação retornam 400 com um corpo
  `ValidationProblem` (lista de erros por campo).
- `GlobalExceptionHandler` (`IExceptionHandler`) captura qualquer exceção não tratada e
  responde com Problem Details (RFC 9457): `DomainException` vira 400 com a mensagem de negócio;
  qualquer outra exceção vira 500 com uma mensagem genérica ("Ocorreu um erro ao processar a
  requisição") — nunca stack trace, nome de classe interna ou mensagem de exceção de
  infraestrutura (ex.: connection string, erro do driver do Postgres) é devolvida ao cliente.

## Rate limiting

`Microsoft.AspNetCore.RateLimiting` (nativo do ASP.NET Core), política de janela fixa
particionada por IP remoto:

| Serviço | Limite padrão | Racional |
|---|---|---|
| Ledger API | 100 req/s por IP | Guarda razoável para o caminho de escrita; não testado por k6 no escopo atual (o alvo de carga do desafio é a consulta). |
| Daily Balance API | 300 req/s por IP | Deliberadamente acima do alvo de 50 RPS do teste de carga, para não interferir na medição de erro/latência, mas ainda limitando abuso. |

Ambos configuráveis via `RateLimiting:PermitLimit`/`RateLimiting:WindowSeconds`. Requisições
acima do limite recebem 429 Too Many Requests.

## CORS

A aplicação Angular é servida de uma origem (`http://localhost:4201`, via nginx) diferente das
duas APIs (`5080`/`5081`), então o navegador aplica a política de mesma origem por padrão. Ambas
as APIs registram `AddCors`/`UseCors` com uma **allowlist fechada** de origens
(`http://localhost:4201` e `http://localhost:4200`, configurável via `Cors:AllowedOrigins`) —
não um wildcard (`AllowAnyOrigin`). Isso permite exatamente o frontend desta solução consumir as
APIs a partir do navegador, sem abrir as APIs para qualquer origem arbitrária.

## TLS/HTTPS e cabeçalhos de segurança

`app.UseHttpsRedirection()` está habilitado em ambos os serviços. No ambiente local via Docker
Compose, o tráfego entre containers roda em HTTP simples (simplificação documentada — não há
certificado configurado para os containers internos); em produção, TLS seria terminado no
Application Gateway/API Management (ver
[07 — Deployment](architecture/07-deployment-local-e-producao.md)), com HSTS e cabeçalhos de
segurança adicionais (`X-Content-Type-Options`, `X-Frame-Options`, etc.) configurados na camada
de borda — não implementados neste repositório.

## Segredos

| Ambiente | Onde os segredos vivem |
|---|---|
| Local (Docker Compose) | Variáveis de ambiente no `docker-compose.yml` — valores de desenvolvimento, nunca reais |
| Produção (proposto) | Azure Key Vault, referenciado via managed identity (ver [07 — Deployment](architecture/07-deployment-local-e-producao.md)) |

`appsettings.json` (o arquivo versionado no repositório) nunca contém um segredo real —
`Jwt:SigningKey` fica vazio nele; credenciais de banco/broker usam valores de exemplo óbvios
(`verity`/`verity`, `guest`/`guest`) que só funcionam contra a instância local do próprio
Compose.

## Princípio do menor privilégio

No ambiente local, o usuário do PostgreSQL (`verity`) tem acesso apenas ao seu próprio banco por
construção (cada serviço só recebe a connection string do seu banco). Em produção, a
recomendação é criar um usuário de banco por serviço, com permissão restrita ao schema/tabelas
daquele contexto, e uma identidade gerenciada por serviço para acesso ao Service Bus/Key Vault —
não implementado neste repositório, tratado como pré-requisito de produção em
[future-evolution.md](future-evolution.md).

## Sanitização de logs

Os logs estruturados (ver [observability.md](observability.md)) nunca incluem o corpo completo
do request/response, tokens de autorização ou credenciais — os campos logados são
deliberadamente escolhidos (`EventId`, `TransactionId`, `CorrelationId`, `BusinessDate`, etc.),
não um dump genérico do payload. Não há, no código atual, um middleware genérico de logging de
request/response que arriscaria capturar dados sensíveis inadvertidamente.

## Dependências e evolução de pipeline

Snyk (SCA — Software Composition Analysis) e SonarQube (qualidade estática) são mencionados no
perfil técnico do desafio e fazem sentido como evolução do pipeline de CI (`.github/workflows/ci.yml`
já cobre build e testes), mas **não estão configurados neste repositório** — adicioná-los é uma
mudança de pipeline, não de código de aplicação, e está descrita como evolução em
[future-evolution.md](future-evolution.md).

## Referências

- [ADR-007 — Segurança e exposição de API](adr/007-seguranca-e-exposicao-de-api.md)
- [Contratos de API](api-contracts.md)
- [Evolução futura](future-evolution.md)
