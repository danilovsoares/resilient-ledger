import { expect, test } from '@playwright/test';
import path from 'node:path';
import { login, PASSWORD, USERNAME } from './helpers';

const SCREENSHOTS_DIR = path.join(__dirname, '..', 'screenshots');

function screenshotPath(name: string): string {
  return path.join(SCREENSHOTS_DIR, name);
}

test.describe('Autenticação', () => {
  test('credenciais inválidas exibem mensagem de erro e permanecem no login', async ({ page }) => {
    await login(page, USERNAME, 'senha-errada');

    await expect(page.getByText('Usuário ou senha inválidos.')).toBeVisible();
    await expect(page).toHaveURL(/\/login$/);
    await page.screenshot({ path: screenshotPath('auth-01-credenciais-invalidas.png'), fullPage: true });
  });

  test('credenciais válidas acessam a área logada', async ({ page }) => {
    await login(page);

    await expect(page).toHaveURL(/\/lancamentos$/);
    await expect(page.getByText('Comerciante')).toBeVisible();
    await page.screenshot({ path: screenshotPath('auth-02-login-ok.png'), fullPage: true });
  });

  test('acessar uma rota protegida sem sessão redireciona para o login', async ({ page }) => {
    await page.goto('/saldo');

    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });

  test('logout encerra a sessão e bloqueia novamente rotas protegidas', async ({ page }) => {
    await login(page);
    await expect(page).toHaveURL(/\/lancamentos$/);

    await page.getByRole('button', { name: 'Sair' }).click();
    await expect(page).toHaveURL(/\/login$/);

    // Confirma que a sessão foi realmente limpa (sessionStorage), não só um redirect de UI.
    await page.goto('/lancamentos');
    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });
});
