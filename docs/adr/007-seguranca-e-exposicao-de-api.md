# ADR 007 - Segurança e exposição de API
Status: Aceita
Data: 2026-09-02

## Contexto

As duas APIs são expostas publicamente (mesmo que hoje só para a aplicação Web do próprio
comerciante) e recebem entrada de usuário (valores, descrições, datas). É preciso uma postura
mínima de segurança sem construir um provedor de identidade completo, que está fora do
escopo do desafio.

O enunciado do desafio pede autenticação/autorização como objetivo geral de segurança, mas
ressalva que essas premissas não precisam estar na codificação, e sim nas decisões e
representações arquiteturais do projeto — os requisitos técnicos obrigatórios (que descartam o
teste se não atendidos) não citam autenticação. Isso orienta a decisão abaixo: autorização real
via JWT Bearer é implementada; a construção de um identity provider completo é uma decisão de
escopo documentada, não uma lacuna.

## Decisão

- **HTTPS em produção** (redirecionamento automático via `UseHttpsRedirection`; em
  desenvolvimento local via Docker Compose, o tráfego roda em HTTP simples entre containers,
  documentado como simplificação local).
- **JWT Bearer** para os endpoints de negócio (`[Authorize]` em `TransactionsController` e
  `DailyBalancesController`), validando emissor, audiência, assinatura e expiração.
- **Login real, sem identity provider externo**: `POST /api/v1/auth/login` (`AuthController`)
  valida usuário/senha contra um `User` persistido na Ledger API (senha com hash `BCrypt`) e
  emite o JWT. Como autenticação corporativa completa (SSO, múltiplos perfis) está fora de
  escopo (ver [01-contexto-e-objetivos.md](../architecture/01-contexto-e-objetivos.md)), não há
  cadastro de usuário — o único usuário do sistema é provisionado na subida por
  `DefaultUserSeeder`, a partir de credencial em configuração. Rate limiting dedicado
  (`login-fixed-window`) mitiga força bruta nesse endpoint especificamente.
- Para os testes de integração, a Ledger API também expõe `POST /api/v1/dev/token`, disponível
  **apenas em ambiente Development**, que emite um token assinado com a mesma chave simétrica
  sem checar identidade — usado só por `WebApplicationFactory`, nunca pela aplicação Web.
- **Rate limiting** (janela fixa por IP, `Microsoft.AspNetCore.RateLimiting`), com limites
  diferentes por serviço: Ledger API 100 req/s, Daily Balance API 300 req/s (deliberadamente
  acima do alvo de 50 RPS do teste de carga, para não interferir na medição — ver
  [performance-and-capacity.md](../performance-and-capacity.md)).
- **Validação de entrada** via FluentValidation (`RegisterTransactionValidator`) e tratamento
  padronizado de erros via Problem Details (`GlobalExceptionHandler`), sem expor stack trace
  ou detalhes internos ao cliente.
- **Segredos fora do repositório**: `Jwt:SigningKey` e as credenciais de banco/broker vêm de
  variável de ambiente localmente (Docker Compose) e de Azure Key Vault em produção (ver
  [07-deployment-local-e-producao.md](../architecture/07-deployment-local-e-producao.md));
  `appsettings.json` nunca contém segredo real.

## Alternativas consideradas

- **Sem autenticação nenhuma nos endpoints**: mais simples, mas deixaria as APIs
  completamente abertas — inaceitável mesmo em um MVP, e contrário ao pedido explícito do
  desafio de JWT Bearer para endpoints protegidos.
- **Nenhuma tela de login, apenas o emissor de token de desenvolvimento** (decisão original
  deste ADR): simples e honesta sobre não existir verificação de identidade, mas deixava a
  jornada do usuário incompleta — evoluída para a opção abaixo a pedido explícito de tornar o
  fluxo de login real, não apenas documentado.
- **Identity provider externo completo (Azure AD B2C, Auth0, Keycloak)**: descartada — o
  desafio marca autenticação corporativa completa como fora de escopo; integrar um provedor
  externo desviaria esforço do problema central (fluxo de caixa) sem agregar valor arquitetural
  demonstrável aqui, além de exigir infraestrutura externa ao ambiente local do desafio.
- **Login próprio, com usuário persistido e senha com hash, sem cadastro** (escolhida): usuário
  final passa por uma tela de login de verdade e o backend valida credenciais reais contra o
  banco — não apenas emite um token sem checar nada. O que fica de fora (cadastro,
  múltiplos perfis, recuperação de senha) é o que o desafio explicitamente trata como fora de
  escopo, não a autenticação em si.

## Consequências positivas

- Endpoints de negócio não são acessíveis sem um token válido, mesmo em ambiente local.
- O login é uma jornada real, não simulada: senha nunca trafega ou é comparada em texto plano
  (hash `BCrypt`), e a resposta de credencial inválida não revela se o usuário existe.
- Rate limiting reduz a superfície de abuso simples (scraping, força bruta) sem depender de
  infraestrutura externa (ex.: um WAF, que é a evolução recomendada em produção).
- Erros nunca vazam detalhes internos (queries SQL, stack trace, nomes de classes internas) —
  apenas título, status e mensagem de negócio quando aplicável.

## Consequências negativas e mitigações

- **O endpoint de emissão de token de desenvolvimento não deve, em hipótese alguma, existir
  em produção.** Mitigação: existe apenas na Ledger API (a Daily Balance API não o expõe) e é
  registrado apenas quando `app.Environment.IsDevelopment()` é verdadeiro (`Program.cs` da
  Ledger API) — nunca mapeado em outros ambientes.
- **Rate limiting por IP é ingênuo** atrás de um proxy/load balancer que mascare o IP real do
  cliente (todos os requests pareceriam vir do mesmo IP). Mitigação: registrado como limitação
  conhecida; em produção, a configuração correta de `X-Forwarded-For`/`ForwardedHeaders` (ou
  delegar rate limiting ao API Management/WAF) é pré-requisito, não implementado aqui.
- **Chave de assinatura JWT simétrica compartilhada entre os dois serviços**: qualquer um dos
  dois pode validar tokens emitidos para o outro. Mitigação: aceitável no escopo atual (mesmo
  domínio de confiança, mesma aplicação); evolução natural seria chaves assimétricas (RS256)
  com um emissor único.

## Critérios de revisão

Revisar obrigatoriamente antes de expor a solução além da aplicação Web do próprio
comerciante (ex.: uma API pública para terceiros) — nesse ponto, um identity provider real
(Azure AD B2C, Auth0, Keycloak) e rate limiting consciente de proxy tornam-se pré-requisitos,
não evoluções opcionais.
