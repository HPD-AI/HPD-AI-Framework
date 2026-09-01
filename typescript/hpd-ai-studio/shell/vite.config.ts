import { svelte } from '@sveltejs/vite-plugin-svelte';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [svelte(), tailwindcss()],
  base: './',
  test: { exclude: ['test-e2e/**', 'node_modules/**'] },
  build: {
    outDir: 'prebuilt',
    emptyOutDir: true,
    manifest: true,
    rollupOptions: {
      output: {
        entryFileNames: 'assets/hpd-studio-shell.js',
        chunkFileNames: 'assets/chunk-[hash].js',
        assetFileNames: asset => asset.names.some(name => name.endsWith('.css')) ? 'assets/hpd-studio-shell.css' : 'assets/[name]-[hash][extname]'
      }
    }
  }
});
