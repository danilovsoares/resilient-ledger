# ADR 006 - Cache e estratégia de leitura
Status: Aceita
Data: 2026-09-02

## Contexto

A Daily Balance API precisa suportar 50 requisições por segundo de consulta com no máximo 5% de
erro. `GET /api/v1/daily-balances/{date}` é uma leitura simples (uma linha por data), mas sob
carga concorrente ainda compensa evitar bater no PostgreSQL a cada requisição, especialmente
quando muitos comerciantes/consultas concentram-se em poucas datas (o dia atual, tipicamente).

## Decisão

Adotar cache-aside com Redis: `GetDailyBalanceHandler` consulta primeiro o Redis
(`daily-balance:{data}`); em caso de hit, responde direto do cache. Em caso de miss, consulta o
PostgreSQL e grava o resultado no Redis com TTL (30 segundos por padrão,
`Redis:TimeToLive`) antes de responder. O `ApplyTransactionHandler`, no Worker, **invalida**
(não atualiza) a chave da data afetada logo após persistir uma atualização na projeção — a
próxima leitura repopula o cache com o valor mais recente.

## Alternativas consideradas

- **Cache-aside com invalidação após escrita** (decisão adotada): simples de raciocinar — o
  Worker não precisa saber o formato exato do DTO de leitura, só precisa invalidar a chave; a
  API sempre lê o dado mais recente do banco no próximo miss.
- **Write-through** (o Worker escreve o novo valor diretamente no cache, sem invalidar):
  evitaria um cache miss logo após a atualização, mas acopla o Worker ao formato de DTO da API
  de leitura e introduz risco de os dois ficarem dessincronizados se o DTO mudar em um lado e
  não no outro. Descartada pela simplicidade e desacoplamento superiores da invalidação.
- **Sem cache, direto no PostgreSQL**: mais simples, mas arrisca não atingir a meta de 50 RPS
  com margem confortável sob picos, e adiciona carga desnecessária a um banco que também
  precisa absorver a escrita da projeção pelo Worker.

## Consequências positivas

- Latência de leitura tipicamente sub-milissegundo em cache hit, medida localmente (ver
  [performance-and-capacity.md](../performance-and-capacity.md)).
- Reduz a carga no PostgreSQL do Daily Balance sob consultas repetidas à mesma data.
- Desacopla o formato interno de persistência do formato de cache — cada um pode evoluir
  independentemente, já que o cache guarda o DTO de resposta, não a entidade de domínio.

## Consequências negativas e mitigações

- **O cache não elimina consistência eventual — ele a adiciona em mais um ponto**: mesmo com o
  saldo já atualizado no PostgreSQL, uma leitura pode retornar um valor em cache de até TTL
  segundos atrás, se a invalidação para aquela chave específica ainda não tiver ocorrido (ex.:
  race entre o Worker invalidar e um cache miss concorrente repopular com um valor já
  desatualizado). Não afirmamos que o cache elimina consistência eventual — ele convive com
  ela, e o TTL curto (30s) limita o pior caso.
- **Redis é mais um componente de infraestrutura que pode falhar**: `RedisDailyBalanceCache`
  trata qualquer exceção de conexão como cache miss silencioso, e a consulta cai para o
  PostgreSQL — a consulta fica mais lenta, não indisponível (ver
  [resiliency-and-messaging.md](../resiliency-and-messaging.md)).
- **Cache frio após deploy/restart do Redis**: os primeiros requests após uma reinicialização
  do Redis pagam o custo de miss. Mitigação: aceitável dado o volume e a simplicidade da query
  de origem; não há necessidade de warm-up ativo neste porte de solução.

## Critérios de revisão

Revisar se o padrão de acesso mudar (ex.: muitas datas distintas consultadas com baixa
repetição, reduzindo a taxa de acerto do cache) ou se o TTL de 30s se mostrar, na prática,
inadequado — nesse caso ajustar o TTL ou considerar invalidação seletiva por padrão de chave.
