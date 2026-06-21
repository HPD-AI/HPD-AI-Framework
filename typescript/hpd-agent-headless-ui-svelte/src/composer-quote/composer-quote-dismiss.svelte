<svelte:options runes={true} />

<script lang="ts">
  import { getComposerQuoteContext } from './context.js';
  import { createComposerQuoteDismissElementProps } from './props.js';
  import type { ComposerQuoteDismissProps } from './types.js';

  let {
    children,
    label = 'Dismiss quote',
    onClear,
    ...restProps
  }: ComposerQuoteDismissProps = $props();

  const context = getComposerQuoteContext();

  function clear(event: MouseEvent): void {
    event.preventDefault();
    context.clear();
    onClear?.();
  }

  const elementProps = $derived(createComposerQuoteDismissElementProps({
    label,
    onClick: clear,
    restProps,
  }));
</script>

{#if context.quote}
  <button {...elementProps}>
    {#if children}
      {@render children({ clear: context.clear, props: elementProps, quote: context.quote })}
    {:else}
      Dismiss
    {/if}
  </button>
{/if}
