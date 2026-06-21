<script lang="ts">
  import ThreadComposer from '../../src/thread-composer/thread-composer.svelte';
  import type { ThreadState } from '../../src/index.js';

  let { thread }: { thread: ThreadState } = $props();
  let value = $state('');
  let textareaRef = $state<HTMLTextAreaElement | null>(null);

  export function getValue(): string {
    return value;
  }

  export function getTextareaRef(): HTMLTextAreaElement | null {
    return textareaRef;
  }

  export function setValue(nextValue: string): void {
    value = nextValue;
  }
</script>

<ThreadComposer {thread} bind:value bind:textareaRef autosize={false} />
