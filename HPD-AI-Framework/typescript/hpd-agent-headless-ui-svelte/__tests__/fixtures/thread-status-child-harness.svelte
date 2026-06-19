<script lang="ts">
  import {
    ThreadStatus,
    type ThreadState,
  } from '../../src/index.js';

  let { thread }: { thread: ThreadState } = $props();
</script>

<ThreadStatus {thread}>
  {#snippet child(status)}
    <aside {...status.props} data-testid="custom-status">
      {status.state}:{status.label}
    </aside>
  {/snippet}
</ThreadStatus>
