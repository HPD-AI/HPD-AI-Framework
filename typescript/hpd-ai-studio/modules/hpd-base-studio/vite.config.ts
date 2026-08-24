import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [svelte()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    lib: { entry: 'src/index.ts', formats: ['es'], fileName: () => 'base.js' },
    cssCodeSplit: false,
    rollupOptions: { output: { assetFileNames: asset => asset.name?.endsWith('.css') ? 'base.css' : '[name]-[hash][extname]' } }
  }
});
