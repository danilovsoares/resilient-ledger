import { expect, test } from '@playwright/test';
import path from 'node:path';
import { fillAmount, isoDateUnique, login, seedTransactions } from './helpers';

const SCREENSHOTS_DIR = path.join(__dirname, '..', 'screenshots');

function screenshotPath(name: string): string {
  return path.join(SCREENSHOTS_DIR, name);
}

test.describe('Saldo Diário', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
    await expect(page).toHaveURL(/\/lancamentos$/);
  });

  test('saldo diário reflete um lançamento recém-registrado', async ({ page }) => {
    await fillAmount(page, '15000');
    await page.getByLabel('Data/hora de ocorrência').fill(`${isoDateUnique()}T12:00`);
    await page.getByLabel('Descrição (opcional)').fill(`E2E saldo ${Date.now()}`);
    await page.getByRole('button', { name: 'Registrar lançamento' }).click();
    await expect(page.getByLabel('Valor')).toHaveValue(/^R\$\s*0,00$/, { timeout: 15_000 });

    await page.getByRole('link', { name: 'Saldo Diário' }).click();
    await expect(page).toHaveURL(/\/saldo$/);
    await expect(page.getByRole('heading', { name: 'Saldo diário consolidado' })).toBeVisible();

    // O saldo é consolidado de forma assíncrona (Outbox -> RabbitMQ -> Worker), então
    // reconsultamos até "Última atualização" aparecer em vez de assumir consistência imediata
    // (ver docs/architecture/05-fluxos-principais.md).
    await expect(async () => {
      await page.getByRole('button', { name: /Atualizar/ }).click();
      await expect(page.getByText('Última atualização:')).toBeVisible();
    }).toPass({ timeout: 30_000, intervals: [1_000, 2_000, 3_000] });

    await expect(page.getByText('Total de créditos')).toBeVisible();
    await page.screenshot({ path: screenshotPath('daily-balance-01-saldo-consolidado.png'), fullPage: true });
  });

  test('trocar a data consultada exibe o saldo exato daquela data', async ({ page }) => {
    // Data isolada e única, só deste teste — permite afirmar o total exato, sem interferência
    // de outros lançamentos (nem de reexecuções anteriores do próprio teste).
    const isolatedDate = isoDateUnique();
    await seedTransactions([
      { type: 1, amount: 321, occurredAt: `${isolatedDate}T09:00:00Z`, description: 'E2E saldo isolado credito' },
      { type: 2, amount: 21, occurredAt: `${isolatedDate}T09:05:00Z`, description: 'E2E saldo isolado debito' },
    ]);

    await page.getByRole('link', { name: 'Saldo Diário' }).click();
    await expect(page).toHaveURL(/\/saldo$/);
    // Espera o componente da nova rota terminar de renderizar antes de tocar no filtro de
    // data — evita interagir com o painel de Lançamentos ainda sendo desmontado pelo router.
    await expect(page.getByRole('heading', { name: 'Saldo diário consolidado' })).toBeVisible();

    const dateInput = page.getByLabel('Data', { exact: true });
    await dateInput.fill(isolatedDate);
    // Confirma que o binding do Angular já processou o novo valor antes de seguir — evita
    // consultar "Atualizar" enquanto o componente ainda está com a data anterior.
    await expect(dateInput).toHaveValue(isolatedDate);

    // Espera os dois eventos (crédito e débito) serem consolidados — não só o primeiro a
    // chegar — antes de conferir os totais exatos.
    await expect(async () => {
      await page.getByRole('button', { name: /Atualizar/ }).click();
      await expect(page.locator('dd.credit')).toHaveText('321,00');
      await expect(page.locator('dd.debit')).toHaveText('21,00');
    }).toPass({ timeout: 30_000, intervals: [1_000, 2_000, 3_000] });

    await expect(page.locator('dd.balance')).toHaveText('300,00');
    await page.screenshot({ path: screenshotPath('daily-balance-02-saldo-data-isolada.png'), fullPage: true });
  });
});
