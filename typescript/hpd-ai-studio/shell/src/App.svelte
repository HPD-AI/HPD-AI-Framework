<script lang="ts">
  import { onMount } from 'svelte';
  import StudioShell from './studio/shell/StudioShell.svelte';
  import { readRuntimeConfig } from './studio/config/runtimeConfig';
  import { createStudioState } from './studio/state/studioState.svelte';
  import { agentStudioModule } from '@hpd-research/hpd-agent-studio';
  import { authStudioModule } from '@hpd-research/hpd-auth-studio';
  import { baseStudioModule } from '@hpd-research/hpd-base-studio';
  import { graphStudioModule } from '@hpd-research/hpd-graph-studio';
  import { mlStudioModule } from '@hpd-research/hpd-ml-studio';
  import { ragStudioModule } from '@hpd-research/hpd-rag-studio';

  const config = readRuntimeConfig();
  const studio = createStudioState({
    config,
    modules: [agentStudioModule, graphStudioModule, ragStudioModule, authStudioModule, mlStudioModule, baseStudioModule]
  });

  let Page = $derived(studio.currentRoute.component);

  onMount(() => {
    studio.syncRouteFromLocation();
  });
</script>

<svelte:window onhashchange={() => studio.syncRouteFromLocation()} />

<StudioShell {studio}>
  {#snippet main()}
    <Page />
  {/snippet}
</StudioShell>
