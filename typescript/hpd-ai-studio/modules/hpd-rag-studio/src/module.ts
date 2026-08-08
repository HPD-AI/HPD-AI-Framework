import RagModulePlaceholder from './RagModulePlaceholder.svelte';
import type { StudioModule } from '@hpd-research/hpd-studio-core';

export const ragStudioModule: StudioModule = {
  id: 'rag',
  label: 'RAG',
  title: 'HPD RAG Studio',
  description: 'Retrieval and grounding workbench surface. The internal page structure is intentionally unset.',
  navItems: [{ path: '/rag', label: 'RAG' }],
  routes: [
    {
      path: '/rag',
      component: RagModulePlaceholder,
      title: 'RAG',
      eyebrow: 'HPD RAG Studio',
      summary: 'RAG module is active; page scaffolding is intentionally paused.'
    }
  ]
};
