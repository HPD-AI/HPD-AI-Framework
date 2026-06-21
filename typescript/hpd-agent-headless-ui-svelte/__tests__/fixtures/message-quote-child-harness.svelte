<script lang="ts">
  import MessageQuote from '../../src/message-quote/message-quote.svelte';
  import type { Message } from '@hpd-research/hpd-agent-headless-ui';
  import type { ThreadQuote } from '../../src/selection-toolbar/index.js';

  let {
    message,
    quote,
  }: {
    message?: Message;
    quote?: ThreadQuote | null;
  } = $props();
</script>

<MessageQuote {message} {quote}>
  {#snippet children({ props, quote })}
    <aside {...props} data-testid="custom-message-quote">
      <strong>{quote.messageId ?? 'unknown'}</strong>
      <span>{quote.text}</span>
    </aside>
  {/snippet}
</MessageQuote>
