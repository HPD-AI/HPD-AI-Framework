<script lang="ts">
  import Message from '../../src/message/message.svelte';
  import type { Message as ThreadMessage } from '@hpd-research/hpd-agent-headless-ui';

  let { message }: { message: ThreadMessage } = $props();
</script>

<Message {message}>
  {#snippet children({ message, status })}
    <span data-testid="custom-content">{message.role}:{status}:{message.content}</span>
  {/snippet}
</Message>
