<script lang="ts">
  import type { Snippet } from 'svelte';
  import { provideStudioShell, type StudioRouteObservation, type StudioRuntime } from '@hpd-research/hpd-studio-core';
  import Sidebar from './Sidebar.svelte';

  let { studio, observation, main }: { studio: StudioRuntime; observation: StudioRouteObservation; main?: Snippet } = $props();
  provideStudioShell(Object.freeze({
    get configuration() { return studio.configuration; },
    get authentication() { return studio.authentication; },
    currentModuleId: () => observation.route?.moduleId ?? null,
    navigate: (path: string) => { globalThis.location.hash = path; }
  }));
</script>

<div class="grid min-h-screen grid-cols-1 bg-studio-bg text-studio-ink lg:grid-cols-[var(--spacing-studio-sidebar)_minmax(0,1fr)]">
  <Sidebar {studio} {observation} />
  <section class="grid min-h-0 min-w-0 lg:min-h-screen">{@render main?.()}</section>
</div>
