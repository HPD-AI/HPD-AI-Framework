<script lang="ts">
  import type { StudioArea } from '@hpd-research/hpd-studio-core';
  import type { StudioShellRuntime, StudioShellState } from '../shell-runtime.ts';
  let { runtime, state }: { runtime: StudioShellRuntime; state: StudioShellState } = $props();
  const areaOrder: readonly StudioArea[] = ['overview', 'data', 'operations', 'automations', 'subjects', 'search', 'security', 'infrastructure', 'diagnostics'];
  let landings = $derived(areaOrder.flatMap(area => {
    const page = state.bootstrap?.pages.find(item => item.area === area && item.navigationRole === 'areaLanding');
    if (!page) return [];
    const path = `/${page.route.segments.map(segment => segment.kind === 'literal' ? segment.value : '').join('/')}`;
    return [{ area, page, path }];
  }));
</script>

<aside class="studio-task-navigation bg-studio-nav text-studio-nav-ink">
  <button class="flex items-center gap-3 text-left text-inherit" type="button" onclick={() => landings[0] && runtime.navigate(landings[0].path)}>
    <span class="grid size-9 place-items-center rounded-studio-sm bg-studio-brand text-studio-nav font-extrabold">H</span>
    <span class="min-w-0"><strong class="studio-truncate block text-base font-extrabold">HPD BASE Studio</strong>
      <small class="studio-truncate mt-0.5 block text-xs text-studio-nav-muted">{state.bootstrap?.mode === 'inspect' ? 'Inspect' : 'Operate'}</small></span>
  </button>
  <nav class="studio-nav-divider studio-task-area-list pt-4" aria-label="Studio task areas">
    {#each landings as item (item.area)}
      <button class="studio-nav-item text-left capitalize" type="button" onclick={() => runtime.navigate(item.path)}
        aria-current={state.route?.route.page.area === item.area ? 'page' : undefined}>{item.area}</button>
    {/each}
  </nav>
  {#if state.quarantinedModuleIds.length > 0}
    <p class="studio-text-safe text-xs text-studio-nav-muted" role="status">{state.quarantinedModuleIds.length} optional module unavailable</p>
  {/if}
  <section class="studio-nav-divider mt-auto grid gap-2 pt-4" aria-label="Authentication session">
    <span class="studio-label-on-nav">Session</span><span class="text-xs text-studio-nav-muted">Authenticated generation {state.session.principalGeneration}</span>
    <button class="studio-nav-control text-left" type="button" onclick={() => runtime.authentication.beginSignOut()}>Sign out</button>
  </section>
</aside>
