<svelte:options runes={true} />

<script lang="ts">
  import type { Attachment } from 'svelte/attachments';
  import { getThreadTimelineViewportContext } from './context.js';
  import { createThreadTimelineViewportFooterElementProps } from './props.js';
  import type {
    ThreadTimelineViewportApi,
    ThreadTimelineViewportFooterProps,
  } from './types.js';

  let {
    children,
    ...restProps
  }: ThreadTimelineViewportFooterProps = $props();

  const viewport = getOptionalViewport();
  const insetId = `thread-viewport-footer-${nextFooterId()}`;
  const elementProps = $derived(createThreadTimelineViewportFooterElementProps(restProps));

  const footerAttachment: Attachment<HTMLElement> = (node) => {
    if (!viewport) return;

    const measure = (): void => {
      viewport.registerContentInset(insetId, node.getBoundingClientRect().height);
    };

    measure();

    const resizeObserver =
      typeof ResizeObserver === 'undefined'
        ? null
        : new ResizeObserver(measure);
    resizeObserver?.observe(node);

    return () => {
      resizeObserver?.disconnect();
      viewport.unregisterContentInset(insetId);
    };
  };

  function getOptionalViewport(): ThreadTimelineViewportApi | null {
    try {
      return getThreadTimelineViewportContext();
    } catch {
      return null;
    }
  }
</script>

<div {...elementProps} {@attach footerAttachment}>
  {#if children}
    {@render children({ props: elementProps })}
  {/if}
</div>

<script lang="ts" module>
  let footerId = 0;

  function nextFooterId(): number {
    footerId += 1;
    return footerId;
  }
</script>
