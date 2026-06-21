<script lang="ts">
  import Message from '../../src/message/message.svelte';
  import type { Message as ThreadMessage } from '@hpd-research/hpd-agent-headless-ui';

  let { message }: { message: ThreadMessage } = $props();

  export function setMessage(nextMessage: ThreadMessage): void {
    message = nextMessage;
  }
</script>

<Message {message}>
  {#snippet children({ message, status })}
    <span data-testid="content-output">{message.content}</span>
    <span data-testid="streaming-output">{String(message.streaming)}</span>
    <span data-testid="status-output">{status}</span>
  {/snippet}
</Message>
