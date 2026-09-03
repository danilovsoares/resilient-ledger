import { expect, test } from '@playwright/test';
import path from 'node:path';

const USERNAME = process.env.E2E_USERNAME ?? 'comerciante';
const PASSWORD = process.env.E2E_PASSWORD ?? 'verity123';
const SCREENSHOTS_DIR = path.join(__dirname, '..', 'screenshots');

function screenshotPath(name: string): string {
  return path.join(SCREENSHOTS_DIR, name);
}

test.describe('Jornada do comerciante', () => {
  test('login, registra um lançamento e confere o saldo diário', async ({ page }) => {
    const marker = `E2E ${Date.now()}`;

    await test.step('Login', async () => {
      await page.goto('/login');
      await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible();

      await page.getByLabel('Usuário').fill(USERNAME);
      await page.getByLabel('Senha').fill(PASSWORD);
      await page.screenshot({ path: screenshotPath('01-login.png'), fullPage: true });

      await page.getByRole('button', { name: 'Entrar' }).click();
      await expect(page).toHaveURL(/\/lancamentos$/);
    });

    await test.step('Tela de lançamentos', async () => {
      await expect(page.getByRole('heading', { name: 'Lançamentos', exact: true })).toBeVisible();
      await page.screenshot({ path: screenshotPath('02-lancamentos.png'), fullPage: true });
    });

    await test.step('Registrar um novo lançamento de crédito', async () => {
      const today = new Date().toISOString().slice(0, 10);
      const valorField = page.getByLabel('Valor');

      // Máscara de moeda: dígitos entram da direita para a esquerda (ver
      // currency-mask.directive.ts) — "15000" digitado vira R$ 150,00. Limpa o campo antes
      // (Ctrl+A/Backspace) e usa pressSequentially (tecla a tecla) em vez de fill(), porque o
      // campo tem um ControlValueAccessor customizado que reage a cada evento "input" —
      // preencher o valor inteiro de uma vez em cima do "R$ 0,00" pré-existente embaralha os
      // dígitos.
      await valorField.click();
      await valorField.press('ControlOrMeta+A');
      await valorField.press('Backspace');
      await valorField.pressSequentially('15000', { delay: 20 });
      // Intl.NumberFormat pt-BR insere um espaço não separável entre "R$" e o número.
      await expect(valorField).toHaveValue(/^R\$\s*150,00$/);

      // Horário fixo e cedo do dia: garante que o novo lançamento fica ordenado antes dos
      // demais lançamentos de demonstração já existentes na mesma data de negócio (a lista é
      // ordenada por OccurredAt, ver TransactionRepository), então aparece na primeira página.
      await page.getByLabel('Data/hora de ocorrência').fill(`${today}T00:05`);
      await page.getByLabel('Descrição (opcional)').fill(marker);
      await page.screenshot({ path: screenshotPath('03-novo-lancamento-preenchido.png'), fullPage: true });

      await page.getByRole('button', { name: 'Registrar lançamento' }).click();

      await expect(page.getByRole('cell', { name: marker })).toBeVisible({ timeout: 15_000 });
      await page.screenshot({ path: screenshotPath('04-lancamento-registrado.png'), fullPage: true });
    });

    await test.step('Consultar o saldo diário consolidado', async () => {
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
      await page.screenshot({ path: screenshotPath('05-saldo-diario.png'), fullPage: true });
    });
  });
});
