<script lang="ts">
  import Message from '../../src/message/message.svelte';
  import type { Message as ThreadMessage } from '@hpd-research/hpd-agent-headless-ui';

  let { message }: { message: ThreadMessage } = $props();
</script>

<Message {message}>
  {#snippet child({ props, message, status })}
    <article {...props} data-testid="custom-message">
      <strong>{message.role}</strong>
      <span>{status}</span>
    </article>
  {/snippet}
</Message>
