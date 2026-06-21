<svelte:options runes={true} />

<script lang="ts">
  import {
    getComposerTriggerPopoverContext,
  } from './context.js';
  import {
    createComposerTriggerBackElementProps,
  } from './props.js';
  import type { ComposerTriggerBackProps } from './types.js';

  let {
    children,
    ...restProps
  }: ComposerTriggerBackProps = $props();

  const popover = getComposerTriggerPopoverContext();

  function select(): void {
    popover.setCategory(null);
  }

  function handleClick(event: MouseEvent): void {
    event.preventDefault();
    select();
  }

  const elementProps = $derived(createComposerTriggerBackElementProps({
    onClick: handleClick,
    restProps,
  }));
</script>

{#if children}
  {@render children({ props: elementProps, select })}
{:else}
  <button {...elementProps}>Back</button>
{/if}
