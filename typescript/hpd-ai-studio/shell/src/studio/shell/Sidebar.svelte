<script lang="ts">
  import { onMount } from 'svelte';
  import type { StudioAuthenticationSnapshot, StudioRouteObservation, StudioRuntime } from '@hpd-research/hpd-studio-core';

  let { studio, observation }: { studio: StudioRuntime; observation: StudioRouteObservation } = $props();
  let activeModule = $derived(studio.modules.find((module) => module.id === observation.route?.moduleId));
  let authentication: StudioAuthenticationSnapshot = $state({ isAuthenticated: false });

  onMount(() => studio.authentication.subscribe(value => authentication = value));

  function selectModule(moduleId: string) {
    const route = studio.modules.find((module) => module.id === moduleId)?.routes[0];
    if (route) globalThis.location.hash = route.path;
  }
</script>

<aside class="flex flex-col gap-6 bg-studio-nav px-5 py-5 text-studio-nav-ink">
  <a class="flex items-center gap-3 text-inherit no-underline" href={studio.routes[0] ? `#${studio.routes[0].path}` : '#/'} aria-label={studio.configuration.productTitle}>
    <span class="grid size-9 place-items-center rounded-studio-sm bg-studio-brand text-studio-nav font-extrabold">H</span>
    <span class="min-w-0">
      <strong class="studio-truncate block text-base font-extrabold">{studio.configuration.productTitle}</strong>
      <small class="studio-truncate mt-0.5 block text-xs text-studio-nav-muted">{studio.configuration.apiBasePath ?? 'Local Studio'}</small>
    </span>
  </a>

  {#if studio.modules.length > 0}
    <label class="grid gap-2">
      <span class="studio-label-on-nav">Module</span>
      <select class="studio-nav-control" value={activeModule?.id ?? ''} onchange={(event) => selectModule(event.currentTarget.value)} aria-label="Studio module">
        {#each studio.modules as module (module.id)}
          <option value={module.id}>{module.label}</option>
        {/each}
      </select>
    </label>
  {/if}

  {#if activeModule && activeModule.navItems.length > 0}
    <nav class="studio-nav-divider grid gap-1 pt-4" aria-label={`${activeModule.title} navigation`}>
      <span class="studio-label-on-nav mb-1">{activeModule.title}</span>
      {#each activeModule.navItems as item}
        <a class="studio-nav-item" href={`#${item.path}`} title={item.summary} aria-current={observation.route?.path === item.path ? 'page' : undefined}>
          {item.label}
        </a>
      {/each}
    </nav>
  {/if}

  <section class="studio-nav-divider mt-auto grid gap-2 pt-4" aria-label="Authentication session">
    <span class="studio-label-on-nav">Session</span>
    <span class="studio-text-safe text-xs text-studio-nav-muted">
      {authentication.isAuthenticated ? `Signed in${authentication.subjectHint ? ` as ${authentication.subjectHint}` : ''}` : 'Signed out'}
    </span>
    {#if authentication.isAuthenticated && studio.authentication.beginSignOut}
      <button class="studio-nav-control text-left" type="button" onclick={() => studio.authentication.beginSignOut?.()}>Sign out</button>
    {/if}
  </section>
</aside>
