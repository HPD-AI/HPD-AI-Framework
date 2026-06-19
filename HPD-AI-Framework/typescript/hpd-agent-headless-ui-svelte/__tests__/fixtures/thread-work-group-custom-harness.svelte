<svelte:options runes={true} />

<script lang="ts">
  import ThreadWorkGroup from '../../src/thread-work-group/thread-work-group.svelte';
  import type { ThreadWorkGroup as WorkGroup } from '@hpd-research/hpd-agent-headless-ui';

  let {
    work,
  }: {
    work: WorkGroup;
  } = $props();
</script>

<ThreadWorkGroup {work}>
  {#snippet workPart({ part, index })}
    <div data-testid="custom-part" data-part-type={part.type} data-index={index}>
      {part.type}
    </div>
  {/snippet}
</ThreadWorkGroup>
