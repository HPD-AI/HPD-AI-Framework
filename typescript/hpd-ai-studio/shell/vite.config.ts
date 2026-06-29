import { svelte } from '@sveltejs/vite-plugin-svelte';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [svelte(), tailwindcss()],
  base: './',
  build: {
    outDir: '../../../dotnet/HPD-AI.Studio/wwwroot',
    emptyOutDir: true
  },
  server: {
    proxy: {
      '/api/hpd': 'http://127.0.0.1:5000'
    }
  }
});
