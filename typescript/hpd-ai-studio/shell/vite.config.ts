import { svelte } from '@sveltejs/vite-plugin-svelte';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vite';
import type { Plugin } from 'vite';
import { createHash } from 'node:crypto';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';

function shellContractIdentity(): string {
  const roots = [
    resolve(import.meta.dirname, 'src'),
    resolve(import.meta.dirname, '../modules/hpd-gateway-studio/src'),
    resolve(import.meta.dirname, '../../hpd-studio-core/src'),
    resolve(import.meta.dirname, '../../hpd-gateway-client/src')
  ];
  const files: string[] = [];
  const visit = (path: string): void => {
    if (statSync(path).isDirectory()) {
      for (const name of readdirSync(path).sort()) visit(resolve(path, name));
    } else if (/\.(css|svelte|ts)$/.test(path)) {
      files.push(path);
    }
  };
  for (const root of roots) visit(root);
  const hash = createHash('sha256');
  hash.update('hpd-studio-shell-contract-v1\0');
  for (const file of files.sort()) {
    const relative = file.replace(resolve(import.meta.dirname, '../../..') + '/', '');
    const bytes = readFileSync(file);
    hash.update(String(Buffer.byteLength(relative)) + ':');
    hash.update(relative);
    hash.update(String(bytes.length) + ':');
    hash.update(bytes);
  }
  return hash.digest('hex');
}

const shellIdentity = shellContractIdentity();

function developmentRuntimeConfig(): string {
  return `globalThis.HPD_STUDIO_CONFIG = ${JSON.stringify({
    apiBasePath: '/api/hpd',
    routePrefix: '/studio',
    productTitle: 'HPD AI Platform',
    mode: 'development',
    assetContractVersion: '1',
    assetIdentity: '0000000000000000000000000000000000000000000000000000000000000000',
    shellContractIdentity: shellIdentity,
    capabilities: [],
    studioModules: []
  })};`;
}

function runtimeConfigPlugin(): Plugin {
  return {
    name: 'hpd-studio-runtime-config',
    configureServer(server) {
      server.middlewares.use((request, response, next) => {
        if (request.url !== '/studio-config.js') return next();
        response.statusCode = 200;
        response.setHeader('Content-Type', 'text/javascript; charset=utf-8');
        response.end(developmentRuntimeConfig());
      });
    },
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: 'studio-config.js', source: developmentRuntimeConfig() });
    }
  };
}

export default defineConfig({
  define: {
    __HPD_STUDIO_SHELL_MARKER__: JSON.stringify(`hpd-shell-contract-v1:${shellIdentity}`)
  },
  plugins: [runtimeConfigPlugin(), svelte(), tailwindcss()],
  base: './',
  build: {
    outDir: '../../../dotnet/HPD-AI.Platform/wwwroot',
    emptyOutDir: true
  },
  server: {
    proxy: {
      '/api/hpd': 'http://127.0.0.1:5000'
    }
  }
});
