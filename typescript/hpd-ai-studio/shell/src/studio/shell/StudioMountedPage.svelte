<script lang="ts">
  import { onDestroy } from 'svelte';
  import type { StudioPageMounter, StudioPageProps } from '@hpd-research/hpd-studio-core';
  import { StudioNotice } from '@hpd-research/hpd-studio-design';

  let { mounter, props }: { mounter: StudioPageMounter; props: StudioPageProps } = $props();
  let target = $state<HTMLDivElement | null>(null);
  let mountedProps: StudioPageProps | null = null;
  let handle: ReturnType<StudioPageMounter> | null = null;
  let failure = $state('');

  $effect(() => {
    if (!target || (mountedProps === props && handle !== null)) return;
    destroy();
    try {
      handle = mounter(Object.freeze({ target, props }));
      mountedProps = props;
      failure = '';
    } catch (error) {
      failure = error instanceof Error ? error.message : 'Module page mount failed.';
    }
  });

  onDestroy(destroy);

  function destroy(): void {
    try { handle?.destroy(); } catch { /* page teardown is isolated */ }
    handle = null;
    mountedProps = null;
  }
</script>

<div class="grid min-h-0" bind:this={target} data-studio-mounted-page></div>
{#if failure}
  <StudioNotice tone="danger" title="Module page unavailable">
    <p class="studio-text-safe">{failure}</p>
  </StudioNotice>
{/if}
