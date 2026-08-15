<script lang="ts">
  import { onMount } from 'svelte';
  import StudioModuleBoundary from '@hpd-research/hpd-studio-core/boundary';
  import type { StudioRouteObservation, StudioRuntime } from '@hpd-research/hpd-studio-core';
  import StudioShell from './studio/shell/StudioShell.svelte';
  import StudioUnavailable from './studio/shell/StudioUnavailable.svelte';

  let { studio }: { studio: StudioRuntime } = $props();
  // svelte-ignore state_referenced_locally
  let observation: StudioRouteObservation = $state(studio.current);
  let Page = $derived(observation.route?.component);

  function syncRouteFromLocation() {
    const hash = globalThis.location?.hash.replace(/^#/, '') ?? '';
    observation = studio.navigate(hash || studio.routes[0]?.path || '/');
  }

  onMount(() => {
    const unsubscribe = studio.subscribe((value) => observation = value);
    syncRouteFromLocation();
    return () => {
      unsubscribe();
      void studio.dispose();
    };
  });
</script>

<svelte:window onhashchange={syncRouteFromLocation} />

<StudioShell {studio} {observation}>
  {#snippet main()}
    {#if Page && observation.route}
      {#key observation.route.context}
        <StudioModuleBoundary context={observation.route.context}>
          <Page />
        </StudioModuleBoundary>
      {/key}
    {:else}
      <StudioUnavailable />
    {/if}
  {/snippet}
</StudioShell>
