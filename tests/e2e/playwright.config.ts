import { defineConfig, devices } from '@playwright/test';

/**
 * Testes E2E rodam contra a stack real (docker-compose): frontend Angular + as duas APIs +
 * Postgres/RabbitMQ/Redis. Não fazem mock de rede — a ideia é validar a jornada ponta a ponta
 * (login -> lançamento -> saldo diário), incluindo a consistência eventual do saldo
 * (Outbox -> RabbitMQ -> Worker), não apenas o comportamento do Angular isolado.
 */
export default defineConfig({
  testDir: './tests',
  outputDir: './test-results',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'playwright-report', open: 'never' }],
  ],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:4201',
    trace: 'on-first-retry',
    video: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
});
