import type { AppManifest } from '../types';
import HelloApp from './HelloApp.svelte';

export const helloManifest: AppManifest = {
    id: 'hello-world',
    name: 'Hello World',
    version: '0.1.0',
    icon: '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z" stroke="currentColor" stroke-width="1.5"/><path d="M7 12c.5 1 1.5 2 2 2s1.5-1 2-2" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><path d="M13 12c.5 1 1.5 2 2 2s1.5-1 2-2" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><circle cx="9.5" cy="9" r="1" fill="currentColor"/><circle cx="14.5" cy="9" r="1" fill="currentColor"/></svg>',
    description: 'Example app demonstrating two-tier C# + Svelte architecture',
    category: 'utilities',
    keywords: ['example', 'demo', 'hello'],
    isolation: {
        enabled: false,
    },
    component: () => Promise.resolve({ default: HelloApp }),
    defaultState: { message: 'World', response: null },
    onMount: async (tab) => { console.log('[HelloApp] mounted, tab:', tab.id); },
    onUnmount: async (tab) => { console.log('[HelloApp] unmounted, tab:', tab.id); },
};
