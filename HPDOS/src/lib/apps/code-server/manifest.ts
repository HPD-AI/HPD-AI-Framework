import type { AppManifest } from '../types';
import { api } from '../../api';

export const codeServerManifest: AppManifest = {
    id: 'code-server',
    name: 'Code Editor',
    version: '1.0.0',
    icon: '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M8 6L3 12l5 6M16 6l5 6-5 6M14 4l-4 16" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>',
    description: 'VS Code in the browser via code-server',
    category: 'development',
    keywords: ['editor', 'vscode', 'code', 'ide'],
    isolation: {
        enabled: true,
        bound: false,
        endpoint: '', // set dynamically by onMount after launch
    },
    // Component is never rendered for isolation apps — the web-fragment takes over.
    component: () => import('./CodeServerPlaceholder.svelte'),
    onMount: async (tab) => {
        try {
            const res = await api('/api/apps/code-server/launch', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    executable: 'bin/code-server',
                    args: ['--port', '{port}', '--auth', 'none', '--disable-telemetry'],
                    urlTemplate: 'http://127.0.0.1:{port}',
                    timeoutSeconds: 30,
                }),
            });
            if (!res.ok) throw new Error(`Launch failed: ${res.statusText}`);
            const { url } = await res.json() as { url: string };
            // Patch endpoint so createFragment() picks up the live URL.
            codeServerManifest.isolation!.endpoint = url;
        } catch (e) {
            console.error('[code-server] Failed to launch:', e);
        }
    },
    onUnmount: async (_tab) => {
        try {
            await api('/api/apps/code-server', { method: 'DELETE' });
        } catch (e) {
            console.warn('[code-server] Failed to stop:', e);
        }
    },
};
