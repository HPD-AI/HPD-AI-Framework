import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  fullyParallel: true,
  reporter: [['list']],
  use: {
    baseURL: 'http://127.0.0.1:6174',
    trace: 'on-first-retry',
  },
  webServer: {
    command: 'npm exec vite -- --config e2e/app/playwright-vite.config.ts --host 127.0.0.1 --port 6174',
    url: 'http://127.0.0.1:6174',
    reuseExistingServer: !process.env.CI,
    timeout: 30_000,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
