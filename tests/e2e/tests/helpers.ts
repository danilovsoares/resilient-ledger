import { expect, type Locator, type Page } from '@playwright/test';

export const USERNAME = process.env.E2E_USERNAME ?? 'comerciante';
export const PASSWORD = process.env.E2E_PASSWORD ?? 'verity123';
export const LEDGER_API_URL = process.env.E2E_LEDGER_API_URL ?? 'http://localhost:5080';

export async function login(page: Page, username = USERNAME, password = PASSWORD): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Usuário').fill(username);
  await page.getByLabel('Senha').fill(password);
  await page.getByRole('button', { name: 'Entrar' }).click();
}

/** Preenche o campo de valor mascarado (ver currency-mask.directive.ts): dígitos entram da
 * direita para a esquerda, então digitar "15000" produz R$ 150,00. Limpa o conteúdo anterior
 * antes de digitar para não embaralhar com o "R$ 0,00" já exibido. */
export async function fillAmount(page: Page, digits: string): Promise<void> {
  const field = page.getByLabel('Valor');
  await field.click();
  await field.press('ControlOrMeta+A');
  await field.press('Backspace');
  await field.pressSequentially(digits, { delay: 20 });
}

/**
 * Clica "Registrar lançamento", confirma pela resposta HTTP que o POST realmente foi aceito
 * (em vez de inferir sucesso só pelo formulário resetar — num runner mais lento dá pra
 * confundir "ainda não terminou" com "falhou"), e então localiza a linha resultante na
 * tabela. Não precisa lidar com paginação aqui: todo chamador registra numa data isolada e
 * única (ver `isoDateUnique`), então o lançamento é sempre o único da página — uma versão
 * anterior tentava "clicar Próxima" quando a linha não aparecia de imediato, mas isso podia
 * pegar o estado de paginação de ANTES da troca de data (ainda mostrando "hoje", com dados
 * acumulados de outros testes) e navegar para a página errada, procurando para sempre no
 * lugar errado.
 */
export async function submitAndFindRow(page: Page, marker: string): Promise<Locator> {
  const [response] = await Promise.all([
    page.waitForResponse(
      (res) => res.request().method() === 'POST' && res.url().includes('/api/v1/transactions'),
      { timeout: 15_000 },
    ),
    page.getByRole('button', { name: 'Registrar lançamento' }).click(),
  ]);

  if (!response.ok()) {
    throw new Error(
      `Falha ao registrar lançamento via UI: HTTP ${response.status()} — ${await response.text()}`,
    );
  }

  // Espera o submit terminar: o formulário volta ao estado inicial após sucesso (ver
  // TransactionsPanelComponent.submit()).
  await expect(page.getByLabel('Valor')).toHaveValue(/^R\$\s*0,00$/, { timeout: 15_000 });

  const row = page.getByRole('row', { name: new RegExp(marker) });
  await expect(row).toBeVisible({ timeout: 15_000 });
  return row;
}

/** Data de negócio isolada, a `offsetDays` dias de hoje (UTC) — usada para cenários que
 * precisam de um dia "só deles", sem interferência de outros testes ou de dados manuais de
 * desenvolvimento que já existem na data de hoje. */
export function isoDateOffset(offsetDays: number): string {
  const date = new Date();
  date.setUTCDate(date.getUTCDate() + offsetDays);
  return date.toISOString().slice(0, 10);
}

/**
 * Data de negócio isolada e praticamente única a cada execução (bem no passado, deslocada
 * pelo timestamp atual) — ao contrário de `isoDateOffset` com um deslocamento fixo, resiste a
 * reexecuções repetidas do mesmo teste no mesmo dia local sem acumular dados de execuções
 * anteriores (o volume do Postgres local não é limpo entre execuções; o CI já sobe um banco
 * novo a cada run, mas isso também protege reexecuções locais).
 */
export function isoDateUnique(): string {
  return isoDateOffset(-(1000 + (Date.now() % 100_000)));
}

async function getAccessToken(): Promise<string> {
  const response = await fetch(`${LEDGER_API_URL}/api/v1/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: USERNAME, password: PASSWORD }),
  });
  if (!response.ok) {
    throw new Error(`Falha ao autenticar via API para seed de dados: HTTP ${response.status}`);
  }
  const body = (await response.json()) as { accessToken: string };
  return body.accessToken;
}

export interface SeedTransaction {
  type: 1 | 2; // TransactionType.Credit = 1, Debit = 2
  amount: number;
  occurredAt: string; // ISO com offset/Z
  description?: string;
}

/** Registra lançamentos diretamente pela API (fora da UI) para preparar o cenário de um teste
 * — ex.: popular 11 lançamentos para testar paginação sem depender de 11 submits de formulário.
 * Isolado da jornada de UI: o objetivo aqui é dado de setup, não o que o cenário verifica. */
export async function seedTransactions(transactions: SeedTransaction[]): Promise<void> {
  const token = await getAccessToken();

  for (const transaction of transactions) {
    const response = await fetch(`${LEDGER_API_URL}/api/v1/transactions`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token}`,
        'Idempotency-Key': crypto.randomUUID(),
      },
      body: JSON.stringify({
        type: transaction.type,
        amount: transaction.amount,
        occurredAt: transaction.occurredAt,
        description: transaction.description ?? null,
      }),
    });

    if (!response.ok) {
      throw new Error(`Falha ao semear lançamento via API: HTTP ${response.status} — ${await response.text()}`);
    }
  }
}
