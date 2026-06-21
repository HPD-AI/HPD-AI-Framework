<svelte:options runes={true} />

<script lang="ts">
  import {
    getComposerTriggerPopoverContext,
  } from './context.js';
  import type { ComposerTriggerCategoriesProps } from './types.js';

  let {
    children,
  }: ComposerTriggerCategoriesProps = $props();

  const popover = getComposerTriggerPopoverContext();
  const categories = $derived(popover.categories);
</script>

{@render children({ categories })}
