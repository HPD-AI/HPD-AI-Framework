<svelte:options runes={true} />

<script lang="ts">
  import { createMessageQuoteElementProps, readMessageQuote } from './props.js';
  import type { MessageQuoteProps } from './types.js';

  let {
    children,
    message,
    quote,
    ...restProps
  }: MessageQuoteProps = $props();

  const resolvedQuote = $derived(quote ?? readMessageQuote(message));
  const elementProps = $derived(resolvedQuote
    ? createMessageQuoteElementProps(resolvedQuote, restProps)
    : null);
</script>

{#if resolvedQuote && elementProps}
  <blockquote {...elementProps}>
    {#if children}
      {@render children({ message, props: elementProps, quote: resolvedQuote })}
    {:else}
      {resolvedQuote.text}
    {/if}
  </blockquote>
{/if}
