# Evolução Futura

## Objetivo do documento

Separar explicitamente o que não entrou no escopo desta solução, mas foi pensado durante o
desenho — para que um avaliador (ou um time futuro) entenda que a ausência é deliberada, não um
esquecimento.

## Escopo

Itens fora do escopo funcional e técnico atual, com o racional de por que ficaram de fora e o
que mudaria para suportá-los.

## Outbox Publisher com múltiplas réplicas do Ledger API

`OutboxPublisherService` roda embutido no processo da Ledger API e não usa nenhum mecanismo de
liderança/lock distribuído. Com uma única réplica (o caso atual), isso não é um problema. Se a
Ledger API escalar horizontalmente (múltiplas réplicas), cada réplica passaria a rodar seu
próprio publicador, e todas tentariam publicar as mesmas mensagens pendentes concorrentemente —
inofensivo para a correção do saldo (a Inbox do lado do consumidor já absorve isso, e o sistema
já é declarado at-least-once), mas desperdiça trabalho e pode gerar reentregas mais frequentes
do que o necessário. A evolução natural seria extrair o publicador para um processo dedicado
(um único worker de publicação, seguindo o mesmo padrão do Daily Balance Worker) ou adicionar
um lock distribuído (`SELECT ... FOR UPDATE SKIP LOCKED` no PostgreSQL é o caminho mais simples)
antes de escalar a Ledger API para mais de uma réplica.

## Multitenancy e segregação por comerciante

O modelo atual assume um único comerciante implícito — não há coluna de identificação de
comerciante em nenhuma tabela. Suportar múltiplos comerciantes exigiria: um `MerchantId` em
`transactions`, `outbox_messages`, `daily_balances` e `processed_messages`; a chave de
idempotência passaria a ser composta (`MerchantId` + `Idempotency-Key`); o cache Redis passaria
a particionar por `MerchantId` na chave; e a autorização (JWT) precisaria carregar o
`MerchantId` como claim, validado em cada request.

## Edição de lançamentos

**Cancelamento já foi implementado** — `POST /api/v1/transactions/{id}/cancel` registra um
estorno (`Transaction.RegisterReversal`): um novo lançamento, de tipo oposto e mesmo valor, que
zera o efeito do original no saldo. O lançamento original nunca é alterado ou removido; por ser
só mais um evento aditivo, não exigiu nenhuma mudança no Daily Balance nem quebrou a premissa do
[ADR-004](adr/004-consistencia-eventual-e-ordenacao.md).

O que continua fora de escopo é **edição de verdade** (mutar valor/tipo/data de um lançamento já
registrado), porque o modelo atual depende de lançamentos serem imutáveis e aditivos. A evolução
planejada: cada lançamento ganharia um `TransactionId` estável e um `EventVersion` incremental;
eventos de edição publicariam uma nova versão referenciando o mesmo `TransactionId`; o consumidor
ignoraria versões antigas ou fora de ordem e aplicaria apenas transições válidas (uma máquina de
estados simples por `TransactionId`). Isso muda `DailyBalance.Apply` de uma soma pura para uma
função que precisa saber desfazer o efeito de uma versão anterior antes de aplicar a nova.

## Auditoria imutável, retenção e LGPD

Não há uma trilha de auditoria dedicada (quem alterou o quê, quando) além do que
`outbox_messages`/`processed_messages` guardam incidentalmente. Uma evolução real de auditoria
exigiria um log de eventos append-only dedicado (possivelmente reaproveitando a própria Outbox
como fonte, com retenção estendida), políticas de retenção explícitas por tipo de dado, e
tratamento de dados pessoais conforme LGPD (o modelo atual não armazena nenhum dado pessoal do
comerciante além do necessário para autenticação, mas isso mudaria com multitenancy real).

## Particionamento por comerciante/data em volumes maiores

`transactions` e `daily_balances` são hoje tabelas simples, sem particionamento. Em volume
significativamente maior (e, principalmente, com multitenancy), particionar `transactions` por
`business_date` (ou por `MerchantId` + data) seria a evolução natural para manter os índices
eficientes e viabilizar rotinas de arquivamento por partição antiga.

## Read replicas e cache distribuído em produção

A leitura da Daily Balance API já é desenhada para escalar horizontalmente (múltiplas réplicas
stateless + cache compartilhado), mas o Redis do ambiente local é uma instância única. Em
produção, Azure Cache for Redis (ou um cluster Redis) e, se o volume de leitura direto ao banco
crescer além do que o cache absorve, réplicas de leitura do PostgreSQL seriam a evolução
natural — sem exigir mudança na camada de Application, que já depende de abstrações
(`IDailyBalanceCache`, `IDailyBalanceRepository`).

## Event schema registry e versionamento formal de contratos

`TransactionRegisteredEvent` hoje é versionado implicitamente pelo assembly compartilhado
(`Verity.Shared.Contracts`) — qualquer mudança de formato exige recompilar e reimplantar os dois
serviços juntos. Um schema registry (ex.: formato Avro/Protobuf com compatibilidade
backward/forward declarada) desacoplaria essa evolução, permitindo que Ledger e Daily Balance
sejam implantados de forma verdadeiramente independente mesmo quando o contrato de evento muda.

## Sagas

Não há, hoje, nenhum processo de negócio distribuído que exija coordenação entre múltiplos
passos com compensação (o fluxo atual é "publica evento, consumidor aplica" — não há um segundo
passo que possa falhar e precisar desfazer o primeiro). Sagas seriam avaliadas apenas se surgir
um processo de negócio real que precise dessa coordenação — introduzi-las agora seria
complexidade sem justificativa concreta.

## Azure Service Bus, AKS/Container Apps e KEDA

Descritos como evolução de infraestrutura em [07 — Deployment](architecture/07-deployment-local-e-producao.md),
não implementados neste repositório (o desafio pede execução local via Docker Compose). A
migração não exige mudança de código de aplicação — RabbitMQ/MassTransit, PostgreSQL e Redis já
são acessados por trás de configuração e abstrações, não de referências diretas à infraestrutura
local.

## Identity provider externo

O login (`POST /api/v1/auth/login`) já é real — credenciais validadas contra um `User`
persistido, senha com hash `BCrypt`, sem o antigo emissor silencioso de token. O que **não** foi
implementado, por ser fora do escopo do desafio (ver
[01-contexto-e-objetivos.md](architecture/01-contexto-e-objetivos.md)), é a integração com um
identity provider externo (Azure AD B2C, Auth0, Keycloak): cadastro de novos usuários,
recuperação de senha, múltiplos perfis/papéis e SSO. Descrito em [security.md](security.md) e
[ADR-007](adr/007-seguranca-e-exposicao-de-api.md) — necessário antes de suportar mais de um
comerciante ou usuário por comerciante.

## Dashboards, SLOs formais e alertas de operação

As métricas necessárias já são emitidas (ver [observability.md](observability.md)), mas não há
dashboards nem regras de alerta configuradas — não existe um backend de observabilidade de
produção real para configurá-los contra. A lista de alertas sugeridos em
[observability.md](observability.md) é o ponto de partida quando essa infraestrutura existir.

## SCA e qualidade estática no pipeline

Snyk (análise de composição de software) e SonarQube (qualidade estática) fazem parte do
repertório do perfil técnico do desafio e são a evolução natural de
`.github/workflows/ci.yml` — hoje o pipeline cobre build e testes, não segurança de dependências
nem métricas de qualidade de código.

## Referências

- [01 — Contexto e Objetivos](architecture/01-contexto-e-objetivos.md)
- [ADR-004 — Consistência eventual e ordenação](adr/004-consistencia-eventual-e-ordenacao.md)
- [ADR-007 — Segurança e exposição de API](adr/007-seguranca-e-exposicao-de-api.md)
