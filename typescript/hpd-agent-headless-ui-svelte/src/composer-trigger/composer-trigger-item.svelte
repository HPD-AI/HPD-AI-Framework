<svelte:options runes={true} />

<script lang="ts">
  import {
    getComposerTriggerPopoverContext,
  } from './context.js';
  import {
    createComposerTriggerItemElementProps,
  } from './props.js';
  import type { ComposerTriggerItemProps } from './types.js';

  let {
    children,
    index,
    item,
    ...restProps
  }: ComposerTriggerItemProps = $props();

  const popover = getComposerTriggerPopoverContext();
  const highlighted = $derived(index !== undefined && popover.getHighlightedIndex() === index);

  async function select(): Promise<void> {
    await popover.selectItem(item);
  }

  function handleClick(event: MouseEvent): void {
    event.preventDefault();
    void select();
  }

  const elementProps = $derived(createComposerTriggerItemElementProps({
    highlighted,
    item,
    onClick: handleClick,
    restProps,
  }));
</script>

{#if children}
  {@render children({ highlighted, item, props: elementProps, select })}
{:else}
  <button {...elementProps}>
    <span>{item.label}</span>
    {#if item.description}
      <span>{item.description}</span>
    {/if}
  </button>
{/if}
