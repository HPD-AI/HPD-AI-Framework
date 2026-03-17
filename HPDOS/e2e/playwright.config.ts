import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  webServer: {
    command: 'bun --config=../bunfig.toml ../index.html',
    url: 'http://localhost:5174',
    reuseExistingServer: !process.env['CI'],
  },
  use: {
    baseURL: 'http://localhost:5174',
  },
});
