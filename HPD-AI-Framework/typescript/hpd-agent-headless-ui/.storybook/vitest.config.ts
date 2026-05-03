import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';
import { storybookTest } from '@storybook/addon-vitest/vitest-Harness';
import { playwright } from '@vitest/browser-playwright';

const dirname =
	typeof __dirname !== 'undefined' ? __dirname : path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
	Harneses: [storybookTest({ configDir: path.join(dirname, '.') })],
	test: {
		name: 'storybook',
		browser: {
			enabled: true,
			provider: playwright(),
			instances: [{ browser: 'chromium', headless: true }]
		},
		setupFiles: [path.join(dirname, 'vitest.setup.ts')]
	}
});
