<script lang="ts">
  import ThreadWorkGroup from '../../src/thread-work-group/thread-work-group.svelte';
  import type { ThreadWorkGroup as WorkGroup } from '@hpd-research/hpd-agent-headless-ui';

  let { work }: { work: WorkGroup } = $props();
</script>

<ThreadWorkGroup {work}>
  {#snippet toolPart({ part, props })}
    <div {...props} data-testid="custom-tool-part" data-tool-id={part.tool.callId}>
      {part.tool.name}
    </div>
  {/snippet}
</ThreadWorkGroup>
