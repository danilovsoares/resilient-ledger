# ADR 001 - Separação de contextos Ledger e Daily Balance
Status: Aceita
Data: 2026-09-02

## Contexto

O requisito não funcional determinante do desafio é: o serviço de lançamentos não pode ficar
indisponível se o consolidado diário cair, e o consolidado precisa suportar 50 RPS com no
máximo 5% de erro. Isso implica que escrita (registrar lançamento) e leitura (consultar
saldo) têm perfis de disponibilidade, carga e criticidade diferentes, e que uma falha em um
não pode se propagar para o outro.

## Decisão

Modelar a solução como **dois serviços independentes**: **Ledger**, dono do registro de
lançamentos (caminho crítico de escrita), e **Daily Balance**, dono da projeção de saldo
diário (caminho de leitura otimizado). Cada um com seu próprio banco de dados
(`verity_ledger` e `verity_daily_balance`) e seu próprio processo de deploy. A comunicação
entre eles é assíncrona, via eventos (ver [ADR-002](002-integracao-assincrona-por-eventos.md)).

## Alternativas consideradas

- **Monolito modular**: um único processo/deploy, com módulos internos para Ledger e Daily
  Balance, comunicando-se em memória. Mais simples de operar (um único ponto de deploy, sem
  necessidade de broker) e com menor custo de infraestrutura. Seria adequada **se** o
  requisito de isolamento de falha não existisse — mas nesse desenho, um problema no módulo
  de consolidado (ex.: uma query lenta, um lock, um bug) tem potencial de degradar o mesmo
  processo que atende ao registro de lançamentos, e escalar a leitura implica escalar a
  escrita junto.
- **Dois serviços separados** (escolhida): maior complexidade operacional (dois deploys,
  dois bancos, um broker de mensagens, necessidade de lidar com consistência eventual) em
  troca de isolamento de falha real e escalabilidade independente entre escrita e leitura.

## Consequências positivas

- O Ledger nunca fica indisponível por causa de um problema no Daily Balance — não há
  nenhuma chamada síncrona do primeiro para o segundo.
- A Daily Balance API pode escalar horizontalmente (e usar cache) para atender aos 50 RPS
  exigidos, sem competir por recursos com o caminho de escrita.
- Cada contexto evolui seu modelo de dados e sua tecnologia de leitura/escrita de forma
  independente (ex.: o Daily Balance poderia migrar para um banco orientado a leitura sem
  afetar o Ledger).

## Consequências negativas e mitigações

- **Consistência eventual**: o saldo consultado pode estar temporariamente atrasado em
  relação aos lançamentos mais recentes. Mitigação: eventos são processados em
  frações de segundo em condições normais (validado localmente, ver
  [performance-and-capacity.md](../performance-and-capacity.md)); a UI comunica isso
  explicitamente ao usuário, sem prometer atualização instantânea.
- **Complexidade operacional adicional**: dois serviços para deployar, monitorar e depurar,
  mais um broker de mensagens. Mitigação: observabilidade com CorrelationId ponta a ponta
  (ver [ADR-005](005-observabilidade-e-correlation-id.md)) e runbook operacional
  (ver [operational-runbook.md](../operational-runbook.md)) reduzem o custo de diagnosticar
  problemas distribuídos.
- **Duplicidade de conceitos** (ex.: tipo de lançamento) entre os dois domínios. Mitigação:
  aceita deliberadamente — cada domínio define seu próprio enum, desacoplado do contrato de
  integração (ver [04-componentes-e-camadas.md](../architecture/04-componentes-e-camadas.md)).

## Critérios de revisão

Revisar esta decisão se: (a) o volume de dados ou de tráfego não justificar mais dois
processos e dois bancos separados; (b) surgir um requisito de consistência forte entre
lançamento e saldo (ex.: bloquear a venda se o saldo ficar negativo em tempo real); ou (c) o
custo operacional de manter dois serviços superar o benefício de isolamento observado em
produção.
