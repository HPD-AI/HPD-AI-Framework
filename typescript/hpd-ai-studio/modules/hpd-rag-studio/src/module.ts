import RagModulePlaceholder from './RagModulePlaceholder.svelte';
import type { StudioModule } from './types';

export const ragStudioModule: StudioModule = {
  id: 'rag',
  label: 'RAG',
  title: 'HPD RAG Studio',
  description: 'Retrieval and grounding workbench surface. The internal page structure is intentionally unset.',
  status: 'active',
  capabilities: ['rag', 'retrieval', 'indexes'],
  navItems: [],
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
