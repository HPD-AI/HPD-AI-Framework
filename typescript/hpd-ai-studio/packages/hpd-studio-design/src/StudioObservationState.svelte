<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { StudioDisplayObservation } from './types.ts';
  let { observation, title = 'Workspace', children }: { observation: StudioDisplayObservation; title?: string; children?: Snippet } = $props();
  const current = $derived(observation.state === 'current' || observation.state === 'stale' || observation.state === 'loading' && observation.hasPrevious);
</script>

<section class="min-w-0" aria-busy={observation.state === 'loading'} aria-label={title}>
  {#if observation.state === 'loading'}
    <p class="studio-status studio-status-info" role="status">Refreshing authorized information…</p>
  {:else if observation.state === 'stale'}
    <p class="studio-status studio-status-warning" role="status">Updates are available. Refresh before performing generation-sensitive actions.</p>
  {:else if observation.state === 'unobserved'}
    <div class="studio-empty" role="status"><strong>Preparing this workspace</strong><span>No authorized observation has completed yet.</span></div>
  {:else if observation.state === 'unavailable' || observation.state === 'denied'}
    <div class="studio-empty" role="status"><strong>Resource unavailable</strong><span>The resource is absent or is not disclosed to this session.</span></div>
  {:else if observation.state === 'unsupported'}
    <div class="studio-empty" role="status"><strong>Capability unavailable</strong><span>This installed application does not support the registered view.</span></div>
  {:else if observation.state === 'failed'}
    <div class="studio-empty" role="alert"><strong>Workspace could not be refreshed</strong><span>Retry the finite observation. No earlier authority is being presented as current.</span></div>
  {/if}
  {#if current}{@render children?.()}{/if}
</section>
