<script lang="ts">
  import type { StudioArea, StudioBootstrapSnapshot } from '@hpd-research/hpd-studio-core';
  import type { StudioShellRuntime, StudioShellState } from '../shell-runtime.ts';
  let { runtime, state }: { runtime: StudioShellRuntime; state: StudioShellState } = $props();

  const moduleLabels: Readonly<Record<string, string>> = Object.freeze({
    agents: 'HPD Agent Studio',
    base: 'HPD Base Studio',
    graph: 'HPD Graph Studio',
    gateway: 'HPD Gateway Studio'
  });
  const areaOrder: readonly StudioArea[] = ['overview', 'data', 'operations', 'automations', 'subjects', 'search', 'security', 'infrastructure', 'diagnostics'];
  let activeModuleId = $derived(state.route?.route.page.moduleId ?? '');
  let pages = $derived(state.bootstrap?.pages ?? []);
  let modules = $derived((state.bootstrap?.modules ?? []).flatMap(module => {
    const candidates = pages.filter(page => page.moduleId === module.moduleId && stablePath(page));
    const page = candidates.find(item => item.pageId === `${module.moduleId}.overview`) ??
      candidates.find(item => item.pageId.endsWith('.overview')) ??
      candidates.find(item => item.pageId.endsWith('.catalog')) ??
      candidates[0];
    if (!page) return [];
    return [{
      moduleId: module.moduleId,
      path: stablePath(page)!,
      label: moduleLabels[module.moduleId] ?? `${titleCase(module.moduleId)} Studio`
    }];
  }));
  let activeModule = $derived(modules.find(item => item.moduleId === activeModuleId) ?? {
    moduleId: activeModuleId,
    path: '/',
    label: moduleLabels[activeModuleId] ?? (activeModuleId ? `${titleCase(activeModuleId)} Studio` : 'HPD Studio')
  });
  let activeHome = $derived(
    modules.find(item => item.moduleId === activeModuleId)?.path ??
    modules.find(item => item.moduleId === 'agents')?.path ??
    '/'
  );
  let baseLandings = $derived(areaOrder.flatMap(area => {
    const page = pages.find(item => item.moduleId === 'base' && item.area === area && item.navigationRole === 'areaLanding');
    const path = page ? stablePath(page) : null;
    return path ? [{ label: titleCase(area), path, area }] : [];
  }));
  let agentSections = $derived([
    { label: 'Agents', pageId: 'agent.catalog' },
    { label: 'Sessions', pageId: 'session.catalog' },
    { label: 'Harnesses', pageId: 'harness.catalog' },
    { label: 'Observability', pageId: 'observability.overview' }
  ].flatMap(section => {
    const page = pages.find(item => item.moduleId === 'agents' && item.pageId === section.pageId);
    const path = page ? stablePath(page) : null;
    return path ? [{ ...section, path }] : [];
  }));
  let activeSections = $derived(activeModuleId === 'agents'
    ? agentSections
    : activeModuleId === 'base'
      ? baseLandings
      : moduleSections(activeModuleId));
  let activePageId = $derived(state.route?.route.page.pageId ?? '');

  function stablePath(page: StudioBootstrapSnapshot['pages'][number]): string | null {
    if (page.route.segments.some(segment => segment.kind !== 'literal') ||
        page.route.query.some(member => member.required)) return null;
    return `/${page.route.segments.flatMap(segment => segment.kind === 'literal' ? [segment.value] : []).join('/')}`;
  }

  function moduleSections(moduleId: string): { label: string; path: string; area?: StudioArea }[] {
    const seen = new Set<string>();
    return pages.flatMap(page => {
      if (page.moduleId !== moduleId || page.route.segments[0]?.kind !== 'literal') return [];
      const head = page.route.segments[0].value;
      const path = stablePath(page);
      if (!path || seen.has(head)) return [];
      seen.add(head);
      return [{ label: titleCase(head), path, area: page.area }];
    });
  }

  function titleCase(value: string): string {
    return value.length === 0 ? value : value.charAt(0).toUpperCase() + value.slice(1).replaceAll('-', ' ');
  }
</script>

<aside class="studio-task-navigation bg-studio-nav text-studio-nav-ink">
  <button class="flex items-center gap-3 text-left text-inherit" type="button" onclick={() => runtime.navigate(activeHome)}>
    <span class="grid size-9 place-items-center rounded-studio-sm bg-studio-brand text-studio-nav font-extrabold">H</span>
    <span class="min-w-0"><strong class="studio-truncate block text-base font-extrabold">{activeModule.label}</strong>
      <small class="studio-truncate mt-0.5 block text-xs text-studio-nav-muted">{state.bootstrap?.mode === 'inspect' ? 'Inspect' : 'Operate'}</small></span>
  </button>
  <nav class="studio-nav-divider studio-task-area-list pt-4" aria-label={`${activeModule.label} navigation`}>
    {#each activeSections as item (item.path)}
      <button class="studio-nav-item text-left" type="button" onclick={() => runtime.navigate(item.path)}
        aria-current={activePageId === ('pageId' in item ? item.pageId : '') ||
          state.route?.route.page.area === ('area' in item ? item.area : '') ? 'page' : undefined}>{item.label}</button>
    {/each}
  </nav>
  {#if modules.length > 0}
    <nav class="studio-nav-divider grid gap-1 pt-4" aria-label="Studio modules">
      <span class="studio-label-on-nav px-3">AI Studio</span>
      {#each modules as item (item.moduleId)}
        <button class="studio-nav-item text-left" type="button" onclick={() => runtime.navigate(item.path)}
          aria-current={state.route?.route.page.moduleId === item.moduleId ? 'page' : undefined}>{item.label}</button>
      {/each}
    </nav>
  {/if}
  {#if state.quarantinedModuleIds.length > 0}
    <p class="studio-text-safe text-xs text-studio-nav-muted" role="status">{state.quarantinedModuleIds.length} optional module unavailable</p>
  {/if}
  <section class="studio-nav-divider mt-auto grid gap-2 pt-4" aria-label="Authentication session">
    <span class="studio-label-on-nav">Session</span><span class="text-xs text-studio-nav-muted">Authenticated generation {state.session.principalGeneration}</span>
    <button class="studio-nav-control text-left" type="button" onclick={() => runtime.authentication.beginSignOut()}>Sign out</button>
  </section>
</aside>
