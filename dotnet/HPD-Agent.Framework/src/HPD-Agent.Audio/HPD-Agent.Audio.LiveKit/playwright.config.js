import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './test',
  timeout: 45_000,
  fullyParallel: false,
  workers: 1,
  reporter: [['line']],
  use: { headless: true },
  projects: [
    { name: 'chromium', use: { browserName: 'chromium' } },
    { name: 'firefox', use: { browserName: 'firefox' } },
    { name: 'webkit', use: { browserName: 'webkit' } },
  ],
});
