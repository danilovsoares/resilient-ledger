import { expect, test } from '@playwright/test';
import path from 'node:path';
import { fillAmount, isoDateUnique, login, seedTransactions, submitAndFindRow } from './helpers';

const SCREENSHOTS_DIR = path.join(__dirname, '..', 'screenshots');

function screenshotPath(name: string): string {
  return path.join(SCREENSHOTS_DIR, name);
}

test.describe('Lançamentos', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
    await expect(page).toHaveURL(/\/lancamentos$/);
  });

  test('formulário não permite registrar lançamento sem valor', async ({ page }) => {
    await expect(page.getByLabel('Valor')).toHaveValue(/^R\$\s*0,00$/);
    await expect(page.getByRole('button', { name: 'Registrar lançamento' })).toBeDisabled();
  });

  test('registra um lançamento de crédito e ele aparece na lista', async ({ page }) => {
    const marker = `E2E credito ${Date.now()}`;

    await fillAmount(page, '15000');
    await expect(page.getByLabel('Valor')).toHaveValue(/^R\$\s*150,00$/);
    // Data isolada e única: garante que o lançamento fica sozinho na sua data de negócio,
    // sem depender de qual página da lista ele cai em meio a dados de "hoje".
    await page.getByLabel('Data/hora de ocorrência').fill(`${isoDateUnique()}T12:00`);
    await page.getByLabel('Descrição (opcional)').fill(marker);
    await page.screenshot({ path: screenshotPath('transactions-01-credito-preenchido.png'), fullPage: true });

    const row = await submitAndFindRow(page, marker);
    await expect(row).toBeVisible();
    await expect(row.getByText('Crédito')).toBeVisible();
    await page.screenshot({ path: screenshotPath('transactions-02-credito-registrado.png'), fullPage: true });
  });

  test('registra um lançamento de débito e ele aparece na lista', async ({ page }) => {
    const marker = `E2E debito ${Date.now()}`;

    // Angular gera o value do <option> internamente (ex.: "1: 2" para TransactionType.Debit) —
    // selecionar pelo rótulo visível é o jeito estável de escolher a opção certa.
    await page.getByLabel('Tipo').selectOption({ label: 'Débito' });
    await fillAmount(page, '3500');
    await expect(page.getByLabel('Valor')).toHaveValue(/^R\$\s*35,00$/);
    await page.getByLabel('Data/hora de ocorrência').fill(`${isoDateUnique()}T12:00`);
    await page.getByLabel('Descrição (opcional)').fill(marker);

    const row = await submitAndFindRow(page, marker);
    await expect(row).toBeVisible();
    await expect(row.getByText('Débito')).toBeVisible();
  });

  test('estorna um lançamento registrado', async ({ page }) => {
    const marker = `E2E estorno ${Date.now()}`;

    await fillAmount(page, '9900');
    await page.getByLabel('Data/hora de ocorrência').fill(`${isoDateUnique()}T12:00`);
    await page.getByLabel('Descrição (opcional)').fill(marker);

    const row = await submitAndFindRow(page, marker);
    await expect(row).toBeVisible();

    await row.getByRole('button', { name: 'Estornar' }).click();
    await page
      .getByRole('alertdialog')
      .getByRole('button', { name: 'Estornar' })
      .click();

    await expect(row.getByText('Estornado')).toBeVisible({ timeout: 15_000 });
    await page.screenshot({ path: screenshotPath('transactions-03-lancamento-estornado.png'), fullPage: true });
  });

  test('pagina os lançamentos quando há mais de 10 no dia', async ({ page }) => {
    // Data isolada e única, só deste teste — evita contagem/ordem afetadas por outros
    // cenários, por dados manuais de desenvolvimento em "hoje", ou por reexecuções anteriores
    // do próprio teste.
    const pageDate = isoDateUnique();
    await seedTransactions(
      Array.from({ length: 11 }, (_, i) => ({
        type: 1 as const,
        amount: 10 + i,
        occurredAt: `${pageDate}T08:${String(i).padStart(2, '0')}:00Z`,
        description: `E2E paginacao ${i + 1}`,
      })),
    );

    const dateInput = page.getByLabel('Data', { exact: true });
    await dateInput.fill(pageDate);
    await expect(dateInput).toHaveValue(pageDate);

    await expect(page.getByText('Página 1 de 2')).toBeVisible();
    await expect(page.locator('tbody tr')).toHaveCount(10);
    await expect(page.getByRole('button', { name: 'Anterior' })).toBeDisabled();
    await page.screenshot({ path: screenshotPath('transactions-04-paginacao-pagina-1.png'), fullPage: true });

    await page.getByRole('button', { name: 'Próxima' }).click();

    await expect(page.getByText('Página 2 de 2')).toBeVisible();
    await expect(page.locator('tbody tr')).toHaveCount(1);
    await expect(page.getByRole('button', { name: 'Próxima' })).toBeDisabled();
    await page.screenshot({ path: screenshotPath('transactions-05-paginacao-pagina-2.png'), fullPage: true });
  });
});
