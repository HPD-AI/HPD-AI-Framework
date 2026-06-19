<script lang="ts">
  import Message from '../../src/message/message.svelte';
  import type { Message as ThreadMessage } from '@hpd-research/hpd-agent-headless-ui';

  let {
    message,
    onCopy,
  }: {
    message: ThreadMessage;
    onCopy: (details: { message: ThreadMessage; text: string }) => void;
  } = $props();
</script>

<Message {message} {onCopy}>
  {#snippet actionBar({ message, actions, props })}
    <div {...props.root} data-testid="custom-action-bar">
      <button {...props.copy} onclick={actions.copy}>
        Copy {message.role}
      </button>
    </div>
  {/snippet}
</Message>
