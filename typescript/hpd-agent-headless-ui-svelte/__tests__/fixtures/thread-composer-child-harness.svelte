<script lang="ts">
  import ThreadComposer from '../../src/thread-composer/thread-composer.svelte';
  import type { ThreadState } from '../../src/index.js';

  let { thread }: { thread: ThreadState } = $props();
</script>

<ThreadComposer {thread} autosize={false}>
  {#snippet child({ state, props })}
    <form {...props.root} data-testid="custom-composer">
      <textarea
        {...props.input}
        {@attach props.inputAttachment}
        data-testid="custom-textarea"
      ></textarea>
      <button {...props.submit} data-testid="custom-submit">
        {state.blockedReason ?? 'ready'}
      </button>
      <button {...props.interrupt} data-testid="custom-interrupt">Interrupt</button>
    </form>
  {/snippet}
</ThreadComposer>
