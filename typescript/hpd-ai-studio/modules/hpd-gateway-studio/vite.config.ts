import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [svelte()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    lib: { entry: 'src/index.ts', formats: ['es'], fileName: () => 'gateway.js' },
    cssCodeSplit: false,
    rollupOptions: {
      external: ['@hpd/gateway-client', '@hpd-research/hpd-studio-core'],
      output: { assetFileNames: asset => asset.name?.endsWith('.css') ? 'gateway.css' : '[name]-[hash][extname]' }
    }
  }
});
