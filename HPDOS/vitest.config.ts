import { defineConfig } from 'vitest/config';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
	plugins: [tailwindcss(), svelte()],
	resolve: {
		preserveSymlinks: true,
		conditions: ['browser'],
	},
	test: {
		environment: 'happy-dom',
		globals: true,
		include: ['tests/**/*.{test,spec}.{ts,js}'],
		setupFiles: ['tests/setup.ts'],
		alias: {
			'@testing-library/svelte': '@testing-library/svelte/svelte5',
		},
	},
});
