import adapter from '@sveltejs/adapter-auto';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

/** @type {import('@sveltejs/kit').Config} */
const config = {
	// Consult https://svelte.dev/docs/kit/integrations
	// for more information about preprocessors
	preprocess: vitePreprocess(),

	kit: {
		// adapter-auto only supports some environments, see https://svelte.dev/docs/kit/adapter-auto for a list.
		// If your environment is not supported, or you settled on a specific environment, switch out the adapter.
		// See https://svelte.dev/docs/kit/adapters for more information about adapters.
		adapter: adapter()
	},

	onwarn: (warning, handler) => {
		// Suppress intentional state_referenced_locally warnings
		// These are cases where we intentionally capture initial values (e.g., defaultValue, toolCall)
		if (warning.code === 'state_referenced_locally') {
			// Allow the warning for these specific intentional cases
			if (
				warning.filename?.includes('input.svelte') ||
				warning.filename?.includes('tool-execution-root.svelte') ||
				warning.filename?.includes('permission-dialog-test.svelte') ||
				warning.filename?.includes('permission-dialog-test-render.svelte') ||
				warning.filename?.includes('tool-execution-test.svelte')
			) {
				return;
			}
		}
		handler(warning);
	}
};

export default config;
