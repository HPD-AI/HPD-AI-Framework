<svelte:options runes={true} />

<script lang="ts">
  import { getSelectionToolbarContext } from './context.js';
  import { createSelectionToolbarQuoteElementProps } from './props.js';
  import type { SelectionToolbarQuoteProps } from './types.js';

  let {
    children,
    label = 'Quote selected text',
    onQuote,
    ...restProps
  }: SelectionToolbarQuoteProps = $props();

  const context = getSelectionToolbarContext();

  function handleClick(event: MouseEvent): void {
    event.preventDefault();
    const quote = context.actions.quote();
    if (quote && context.state.selection) {
      void onQuote?.(quote, context.state.selection);
    }
  }

  const elementProps = $derived(createSelectionToolbarQuoteElementProps({
    label,
    onClick: handleClick,
    restProps,
    state: context.state,
  }));
</script>

<button {...elementProps}>
  {#if children}
    {@render children({
      actions: context.actions,
      props: elementProps,
      quote: context.state.quote,
      selection: context.state.selection,
      state: context.state,
    })}
  {:else}
    Quote
  {/if}
</button>
