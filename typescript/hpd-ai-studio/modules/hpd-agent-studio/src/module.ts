import AgentModulePlaceholder from './AgentModulePlaceholder.svelte';
import type { StudioModule } from './types';

export const agentStudioModule: StudioModule = {
  id: 'agents',
  label: 'Agents',
  title: 'HPD Agent Studio',
  description: 'Agent-specific workbench surface. The internal page structure is intentionally unset.',
  status: 'active',
  capabilities: ['agents', 'sessions', 'threads', 'streaming', 'content', 'multi-agent', 'agent-evals'],
  navItems: [],
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
