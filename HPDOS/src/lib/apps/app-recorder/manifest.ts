import type { AppManifest } from '../types';

const icon = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
  <path d="M15 10l4.553-2.069A1 1 0 0121 8.87v6.26a1 1 0 01-1.447.9L15 14"/>
  <rect x="1" y="6" width="14" height="12" rx="2" ry="2"/>
</svg>`;

export const appRecorderManifest: AppManifest = {
    id: 'hpd-video',
    name: 'HPD Video',
    version: '0.1.0',
    icon,
    description: 'AI-native video editor — record, edit, export',
    category: 'media',
    keywords: ['record', 'screen', 'video', 'edit', 'export', 'gif', 'mp4'],
    backendAppId: 'hpd-video',
    isolation: { enabled: false },
    component: () => import('./AppRecorderApp.svelte').then(m => ({ default: m.default })),
    defaultState: {},
    onMount: async (tab) => { console.log('[HPDVideo] mounted, tab:', tab.id); },
    onUnmount: async (tab) => { console.log('[HPDVideo] unmounted, tab:', tab.id); },
};
