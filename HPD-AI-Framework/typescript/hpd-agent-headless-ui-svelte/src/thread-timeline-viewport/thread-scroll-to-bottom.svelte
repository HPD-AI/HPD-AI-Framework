<svelte:options runes={true} />

<script lang="ts">
  import { getThreadTimelineViewportContext } from './context.js';
  import { createThreadScrollToBottomElementProps } from './props.js';
  import type {
    ThreadScrollToBottomProps,
    ThreadTimelineViewportApi,
  } from './types.js';

  let {
    behavior,
    child,
    children,
    disabled = false,
    ...restProps
  }: ThreadScrollToBottomProps = $props();

  const viewport = getOptionalViewport();
  const atBottom = $derived(viewport?.isAtBottom ?? true);

  function handleClick(event: MouseEvent): void {
    if (!viewport || disabled || viewport.isAtBottom) {
      event.preventDefault();
      return;
    }

    viewport.scrollToBottom({ behavior });
  }

  const elementProps = $derived(createThreadScrollToBottomElementProps({
    atBottom,
    disabled,
    onclick: handleClick,
    restProps,
    viewport,
  }));

  function getOptionalViewport(): ThreadTimelineViewportApi | null {
    try {
      return getThreadTimelineViewportContext();
    } catch {
      return null;
    }
  }
</script>

{#if child}
  {@render child({ props: elementProps, viewport })}
{:else}
  <button {...elementProps}>
    {#if children}
      {@render children({ props: elementProps, viewport })}
    {:else}
      Scroll to bottom
    {/if}
  </button>
{/if}
