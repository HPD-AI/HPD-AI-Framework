import { defineConfig } from 'vitest/config';
import { playwright } from '@vitest/browser-playwright';
import { sveltekit } from '@sveltejs/kit/vite';

export default defineConfig({
	plugins: [sveltekit()],

	optimizeDeps: {
		// Exclude svelte and all libraries that embed Svelte lifecycle APIs ($effect,
		// setContext, getContext, $state, etc.). When pre-bundled by Vite they capture
		// their own copy of the Svelte runtime, creating a second Svelte instance that
		// doesn't share the same scheduler or component context as the test renderer.
		// This causes "lifecycle_outside_component" errors, broken reactivity (state
		// changes not flushing to DOM), and silent click/keyboard handler failures.
		// Excluding them forces all Svelte imports to resolve to the same module instance.
		exclude: ['svelte', 'runed', 'svelte-toolbelt'],
		// Eagerly include fast-check to prevent a mid-run Vite page reload that
		// causes "Failed to fetch dynamically imported module" test failures.
		include: ['fast-check', 'esm-env']
	},

	test: {
		expect: { requireAssertions: true },

		projects: [
			{
				extends: './vite.config.ts',

				test: {
					name: 'client',

					browser: {
						enabled: true,
						provider: playwright(),
						instances: [{ browser: 'chromium', headless: true }]
					},

					include: ['src/**/*.browser.{test,spec}.{js,ts}', 'src/**/*.svelte.{test,spec}.{js,ts}'],
					exclude: ['src/lib/server/**']
				}
			},

			{
				extends: './vite.config.ts',

				test: {
					name: 'server',
					environment: 'node',
					include: ['src/**/*.{test,spec}.{js,ts}'],
					exclude: ['src/**/*.browser.{test,spec}.{js,ts}', 'src/**/*.svelte.{test,spec}.{js,ts}']
				}
			}
		]
	}
});
