import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

const config = {
  compilerOptions: {
    runes: true,
  },
  preprocess: vitePreprocess({ script: true }),
};

export default config;
