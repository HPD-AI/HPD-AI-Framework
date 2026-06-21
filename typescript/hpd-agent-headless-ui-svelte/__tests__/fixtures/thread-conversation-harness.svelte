<script lang="ts">
  import {
    ThreadConversation,
    type ThreadConversationRuntimeRequestPlacement,
    type ThreadState,
  } from '../../src/index.js';

  let {
    runtimeRequestPlacement,
    thread,
  }: {
    runtimeRequestPlacement?: ThreadConversationRuntimeRequestPlacement;
    thread: ThreadState;
  } = $props();
</script>

<ThreadConversation
  {thread}
  {runtimeRequestPlacement}
  data-testid="conversation"
  composerProps={{
    autosize: false,
    runConfig: { modelId: 'story-test' },
  }}
/>
