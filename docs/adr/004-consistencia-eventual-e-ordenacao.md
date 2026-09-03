# ADR 004 - Consistência eventual, ordenação e reprocessamento
Status: Aceita
Data: 2026-09-02

## Contexto

O saldo diário é uma projeção mantida de forma assíncrona ([ADR-002](002-integracao-assincrona-por-eventos.md)).
RabbitMQ não garante ordem estrita de entrega entre consumidores concorrentes, e mesmo que
garantisse, retries e reentregas podem alterar a ordem efetiva de processamento. É preciso
decidir se isso é um problema para o modelo de dados atual.

## Decisão

Tratar o saldo consolidado como uma **projeção eventualmente consistente**, e assumir que,
no escopo inicial, os eventos de lançamento são **imutáveis e aditivos** — um lançamento,
uma vez criado, nunca é editado ou removido; ele só soma (crédito) ou subtrai (débito) do
saldo. Sob essa premissa, `DailyBalance.Apply` é comutativo: aplicar débito-depois-crédito ou
crédito-depois-débito chega ao mesmo saldo final. Portanto, **a ordem de aplicação dos
eventos não importa** para a corretude do resultado atual.

O que importa, e é onde a corretude real mora, é que cada evento seja aplicado **no máximo
uma vez** — isso é responsabilidade da Inbox (ver [ADR-003](003-transactional-outbox-e-inbox.md)),
não da ordenação.

## Alternativas consideradas

- **Garantir ordenação estrita por partição/chave de agregado** (ex.: uma fila por data de
  negócio, ou particionamento por `TransactionId`): descartada para o escopo atual porque
  adiciona complexidade operacional sem benefício, já que o modelo aditivo não depende de
  ordem. Fica registrada aqui como o caminho natural **se** o modelo deixar de ser
  puramente aditivo (ver abaixo).
- **Consistência forte via chamada síncrona**: descartada em [ADR-002](002-integracao-assincrona-por-eventos.md).
- **Aceitar consistência eventual e depender apenas de deduplicação, não de ordenação**
  (escolhida): mais simples, e suficiente para o modelo de dados atual.

## Consequências positivas

- Nenhuma necessidade de coordenar ordem entre consumidores, filas ou instâncias do Worker —
  simplifica a escalabilidade horizontal do consumo.
- Reprocessar o backlog inteiro após uma indisponibilidade do Worker (ver
  [05-fluxos-principais.md](../architecture/05-fluxos-principais.md#5-queda-do-daily-balance-sem-indisponibilizar-o-ledger))
  chega ao mesmo resultado final independentemente da ordem em que as mensagens acumuladas
  forem processadas.

## Consequências negativas e mitigações

- **Duplicidade, ao contrário de desordem, mudaria o resultado** — um evento aplicado duas
  vezes soma duas vezes. Mitigação: bloqueada pela Inbox, não por ordenação (ver ADR-003).
- **A premissa de aditividade não se sustenta se o escopo evoluir** para suportar **edição**
  de lançamentos (mudar valor/tipo/data de um lançamento já existente) — nesse caso, um evento
  de "correção" aplicado antes do evento de "criação" correspondente (por causa de
  reordenação/reentrega) corromperia o saldo. Mitigação planejada, não implementada: para essa
  evolução futura, cada evento carregaria `TransactionId` e `EventVersion` (número sequencial
  por agregado); o consumidor compararia a versão recebida com a última aplicada e
  **ignoraria versões antigas ou fora de ordem**. Isso está descrito como evolução, não como
  código existente — ver [future-evolution.md](../future-evolution.md).
  - **Cancelamento, por outro lado, já está implementado e não precisou dessa mitigação.**
    `Transaction.RegisterReversal` registra um **novo** lançamento (tipo oposto, mesmo valor),
    em vez de mutar ou remover o original — do ponto de vista do Daily Balance é só mais um
    evento aditivo, sem relação de ordem com o evento que está estornando. A premissa deste
    ADR (aditividade, ordem não importa) permanece válida; `EventVersion` continua sendo
    necessário apenas se um dia existir edição de verdade.

## Critérios de revisão

Revisar obrigatoriamente esta decisão antes de implementar **edição** de lançamentos (mutação
do valor/tipo/data de um registro já existente) — nesse momento, `EventVersion` e uma
estratégia explícita de ordenação por agregado deixam de ser "evolução futura" e passam a ser
pré-requisito de corretude. Cancelamento via estorno (já implementado) não aciona este
critério, porque preserva a aditividade.
