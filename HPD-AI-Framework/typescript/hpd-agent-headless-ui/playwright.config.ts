import { defineConfig } from '@playwright/test';

export default defineConfig({
	webServer: { command: 'npx vite build && npx vite preview', port: 4173 },
	testDir: 'e2e'
});
