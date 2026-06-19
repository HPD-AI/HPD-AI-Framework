<svelte:options runes={true} />

<script lang="ts">
  import {
    getComposerTriggerPopoverContext,
  } from './context.js';
  import {
    createComposerTriggerCategoryElementProps,
  } from './props.js';
  import type { ComposerTriggerCategoryProps } from './types.js';

  let {
    category,
    children,
    ...restProps
  }: ComposerTriggerCategoryProps = $props();

  const popover = getComposerTriggerPopoverContext();

  function select(): void {
    popover.setCategory(category.id);
  }

  function handleClick(event: MouseEvent): void {
    event.preventDefault();
    select();
  }

  const elementProps = $derived(createComposerTriggerCategoryElementProps({
    category,
    onClick: handleClick,
    restProps,
  }));
</script>

{#if children}
  {@render children({ category, props: elementProps, select })}
{:else}
  <button {...elementProps}>{category.label}</button>
{/if}
