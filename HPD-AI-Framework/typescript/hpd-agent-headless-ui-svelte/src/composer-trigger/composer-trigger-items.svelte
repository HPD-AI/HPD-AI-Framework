<svelte:options runes={true} />

<script lang="ts">
  import {
    getComposerTriggerPopoverContext,
  } from './context.js';
  import type { ComposerTriggerItemsProps } from './types.js';

  let {
    children,
  }: ComposerTriggerItemsProps = $props();

  const popover = getComposerTriggerPopoverContext();
  const items = $derived(popover.getItems());
</script>

{@render children({ items })}
