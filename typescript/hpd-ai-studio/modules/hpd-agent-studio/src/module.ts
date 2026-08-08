import AgentModulePlaceholder from './AgentModulePlaceholder.svelte';
import type { StudioModule } from '@hpd-research/hpd-studio-core';

export const agentStudioModule: StudioModule = {
  id: 'agents',
  label: 'Agents',
  title: 'HPD Agent Studio',
  description: 'Agent-specific workbench surface. The internal page structure is intentionally unset.',
  navItems: [{ path: '/agents', label: 'Agents' }],
  initialize({ contexts }) {
    contexts.set('agent.selection', Object.seal({
      agentId: '',
      sessionId: '',
      threadId: '',
      runId: '',
      eventId: ''
    }));
  },
  routes: [
    {
      path: '/agents',
      component: AgentModulePlaceholder,
      title: 'Agent Studio',
      eyebrow: 'HPD Agent Studio',
      summary: 'Agent module is active; page scaffolding is intentionally paused.'
    }
  ]
};
