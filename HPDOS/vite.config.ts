import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import tailwindcss from '@tailwindcss/vite';

const backendPort = process.env['HPDOS_BACKEND_PORT'] ?? '5173';

export default defineConfig({
	plugins: [
		tailwindcss(),
		svelte(),
		// Strip crossorigin attrs and convert <script type="module"> → <script defer>
		// in the production HTML output. HybridWebView serves from its own local scheme
		// (https://0.0.0.1/) and crossorigin triggers CORS checks that block resources.
		{
			name: 'strip-crossorigin',
			apply: 'build',
			transformIndexHtml(html) {
				return html
					.replace(/\s+crossorigin(?:="[^"]*")?/g, '')
					.replace(/<script type="module"/g, '<script defer');
			},
		},
	],

	// index.html is at repo root — Vite's default root is '.' so no override needed.

	build: {
		outDir: 'src-dotnet/HPDOS.Core/wwwroot',
		emptyOutDir: true,
		rollupOptions: {
			output: {
				chunkFileNames: '_hpdos/[name]-[hash].js',
				assetFileNames: 'assets/[name]-[hash][extname]',
				entryFileNames: '_hpdos/[name]-[hash].js',
			},
		},
	},

	server: {
		port: 5174,
		proxy: {
			'/api': { target: `http://localhost:${backendPort}`, changeOrigin: true },
			'/agents': { target: `http://localhost:${backendPort}`, changeOrigin: true },
			'/sessions': { target: `http://localhost:${backendPort}`, changeOrigin: true },
		},
	},

	resolve: {
		// Preserve symlinks so the file: dep (@hpd/hpd-agent-headless-ui)
		// finds its own peer deps (svelte) relative to HPDOS/node_modules.
		preserveSymlinks: true,
	},

	optimizeDeps: {
		exclude: ['@hpd/hpd-agent-headless-ui'],
	},
});
