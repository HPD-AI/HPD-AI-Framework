<svelte:options runes={true} />

<script lang="ts">
  import { setComposerQuoteContext } from './context.js';
  import { createComposerQuoteRootElementProps } from './props.js';
  import type { ComposerQuoteProps } from './types.js';

  let {
    children,
    onClear,
    quote = $bindable(null),
    ...restProps
  }: ComposerQuoteProps = $props();

  function clear(): void {
    quote = null;
    onClear?.();
  }

  const elementProps = $derived(quote
    ? createComposerQuoteRootElementProps(quote, restProps)
    : null);

  setComposerQuoteContext({
    clear,
    get props() {
      return { root: elementProps };
    },
    get quote() {
      return quote;
    },
  });
</script>

{#if quote && elementProps}
  <div {...elementProps}>
    {#if children}
      {@render children({ clear, props: elementProps, quote })}
    {:else}
      <span data-hpd-composer-quote-text>{quote.text}</span>
      <button
        aria-label="Dismiss quote"
        data-hpd-composer-quote-dismiss
        type="button"
        onclick={clear}
      >
        Dismiss
      </button>
    {/if}
  </div>
{/if}
