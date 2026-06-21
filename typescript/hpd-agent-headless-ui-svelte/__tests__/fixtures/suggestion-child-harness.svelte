<script lang="ts">
  import {
    Suggestion,
    type ThreadState,
  } from '../../src/index.js';

  let {
    prompt = 'Review this file',
    thread,
  }: {
    prompt?: string;
    thread?: ThreadState;
  } = $props();
</script>

<Suggestion {thread} {prompt} title="Review" mode="send">
  {#snippet child({ actions, blockedReason, props, title })}
    <button
      {...props}
      data-testid="custom-suggestion"
      onclick={() => {
        void actions.select();
      }}
    >
      {title}:{blockedReason ?? 'ready'}
    </button>
  {/snippet}
</Suggestion>
