<svelte:options runes={true} />

<script lang="ts">
  import { tick } from 'svelte';
  import type { Attachment } from 'svelte/attachments';
  import type {
    ThreadTimelineItem,
    ThreadWorkPart,
  } from '@hpd-research/hpd-agent-headless-ui';
  import type { ThreadStateSnapshot } from '../thread-state.js';
  import ThreadTimeline from '../thread-timeline/thread-timeline.svelte';
  import { setThreadTimelineViewportContext } from './context.js';
  import { createThreadTimelineViewportElementProps } from './props.js';
  import {
    computeTopAnchorReserve,
    computeTopAnchorTargetScrollTop,
    parseCssLength,
  } from './top-anchor.js';
  import type {
    ThreadTimelineViewportApi,
    ThreadTimelineViewportProps,
  } from './types.js';

  let {
    ariaLabel = 'Thread timeline',
    anchorBlock = 'start',
    anchorInline = 'nearest',
    atBottomThreshold = 48,
    autoScroll = true,
    children,
    scrollBehavior = 'auto',
    scrollContainer = 'nearest',
    scrollToBottomOnInitialize = true,
    scrollToBottomOnRunStart = true,
    thread,
    timeline: providedTimeline,
    topAnchorMessageClamp = {
      tallerThan: '10em',
      visibleHeight: '6em',
    },
    turnAnchor = 'top',
    ...restProps
  }: ThreadTimelineViewportProps = $props();

  let current = $state<ThreadStateSnapshot | null>(null);
  let viewportNode = $state<HTMLElement | null>(null);
  let isAtBottom = $state(true);
  let previousSignature = '';
  let previousLatestUserMessageId: string | null = null;
  let previousRunId: string | null = null;
  let topAnchorReserveHeight = $state(0);
  const itemElements = new Map<string, HTMLElement>();
  const contentInsets = new Map<string, number>();
  let contentInset = $state(0);

  $effect(() => {
    if (!thread) {
      current = null;
      return;
    }

    current = thread.getSnapshot();
    return thread.subscribe((snapshot) => {
      current = snapshot;
    });
  });

  const timeline = $derived(providedTimeline ?? current?.timeline ?? []);
  const latestUserMessageId = $derived(getLatestUserMessageId(timeline));
  const currentRunId = $derived(getCurrentRunId(current));
  const autoScrollSuppressed = $derived(autoScroll && !isAtBottom);
  const elementProps = $derived(createThreadTimelineViewportElementProps({
    ariaLabel,
    autoScroll,
    autoScrollSuppressed,
    isAtBottom,
    isEmpty: timeline.length === 0,
    restProps,
    turnAnchor,
  }));

  function updateIsAtBottom(node = viewportNode): void {
    if (!node) {
      isAtBottom = true;
      return;
    }

    const distanceFromBottom = node.scrollHeight - node.scrollTop - node.clientHeight - contentInset;
    isAtBottom = distanceFromBottom <= atBottomThreshold;
  }

  function scrollToBottom(options: { behavior?: ScrollBehavior } = {}): void {
    const node = viewportNode;
    if (!node) return;
    setScrollTop(node, Math.max(0, node.scrollHeight - contentInset), options.behavior ?? scrollBehavior);
    updateIsAtBottom(node);
  }

  function scrollToItem(
    id: string,
    options: {
      behavior?: ScrollBehavior;
      block?: ScrollLogicalPosition;
      container?: 'all' | 'nearest';
      inline?: ScrollLogicalPosition;
    } = {},
  ): void {
    const node = viewportNode;
    if (!node) return;

    const item = itemElements.get(id) ?? findItemElement(node, id);
    if (!item) {
      scrollToBottom({ behavior: options.behavior });
      return;
    }

    const behavior = options.behavior ?? scrollBehavior;
    const block = options.block ?? anchorBlock;
    const inline = options.inline ?? anchorInline;
    const container = options.container ?? scrollContainer;

    scrollItemIntoView(item, node, { behavior, block, container, inline });
    updateIsAtBottom(node);
  }

  async function scrollToTopAnchor(id: string): Promise<void> {
    const node = viewportNode;
    if (!node) return;

    const item = itemElements.get(id) ?? findItemElement(node, id);
    if (!item) {
      scrollToBottom();
      return;
    }

    const clamp = {
      tallerThan: parseCssLength(topAnchorMessageClamp.tallerThan, item),
      visibleHeight: parseCssLength(topAnchorMessageClamp.visibleHeight, item),
    };

    const nextReserveHeight = computeTopAnchorReserve({
      anchor: item,
      reserveHeight: topAnchorReserveHeight,
      viewport: node,
      ...clamp,
    });

    if (nextReserveHeight !== topAnchorReserveHeight) {
      topAnchorReserveHeight = nextReserveHeight;
      await tick();
    }

    setScrollTop(node, computeTopAnchorTargetScrollTop({
      anchor: item,
      viewport: node,
      ...clamp,
    }), scrollBehavior);
    updateIsAtBottom(node);
  }

  function registerItem(id: string, element: HTMLElement): void {
    itemElements.set(id, element);
  }

  function unregisterItem(id: string): void {
    itemElements.delete(id);
  }

  function registerContentInset(id: string, height: number): void {
    const nextHeight = Math.max(0, height);
    if (contentInsets.get(id) === nextHeight) return;
    contentInsets.set(id, nextHeight);
    contentInset = sumContentInsets();
  }

  function unregisterContentInset(id: string): void {
    if (!contentInsets.has(id)) return;
    contentInsets.delete(id);
    contentInset = sumContentInsets();
  }

  function sumContentInsets(): number {
    let total = 0;
    for (const height of contentInsets.values()) total += height;
    return total;
  }

  const viewport: ThreadTimelineViewportApi = {
    get autoScrollSuppressed() {
      return autoScrollSuppressed;
    },
    get contentInset() {
      return contentInset;
    },
    get isAtBottom() {
      return isAtBottom;
    },
    registerContentInset,
    registerItem,
    scrollToBottom,
    scrollToItem,
    unregisterContentInset,
    unregisterItem,
  };

  setThreadTimelineViewportContext(viewport);

  const viewportAttachment: Attachment<HTMLElement> = (node) => {
    viewportNode = node;
    updateIsAtBottom(node);

    const handleScroll = (): void => {
      updateIsAtBottom(node);
    };

    node.addEventListener('scroll', handleScroll, { passive: true });

    queueMicrotask(() => {
      if (autoScroll && scrollToBottomOnInitialize) scrollToBottom();
    });

    return () => {
      node.removeEventListener('scroll', handleScroll);
      if (viewportNode === node) viewportNode = null;
    };
  };

  $effect.pre(() => {
    const node = viewportNode;
    const nextSignature = getTimelineScrollSignature(timeline);
    const nextLatestUserMessageId = latestUserMessageId;
    const nextRunId = currentRunId;
    const previousUserMessageId = previousLatestUserMessageId;
    const previousObservedRunId = previousRunId;
    const signatureChanged = nextSignature !== previousSignature;
    const latestUserMessageChanged =
      nextLatestUserMessageId !== null &&
      nextLatestUserMessageId !== previousUserMessageId;
    const runStarted =
      nextRunId !== null &&
      nextRunId !== previousObservedRunId;

    previousSignature = nextSignature;
    previousLatestUserMessageId = nextLatestUserMessageId;
    previousRunId = nextRunId;

    if (!node || !autoScroll || !signatureChanged) return;

    const shouldScroll =
      isAtBottom ||
      latestUserMessageChanged ||
      (scrollToBottomOnRunStart && runStarted);

    if (!shouldScroll) return;

    void tick().then(() => {
      if (turnAnchor === 'top' && latestUserMessageChanged && nextLatestUserMessageId) {
        void scrollToTopAnchor(nextLatestUserMessageId);
        return;
      }

      scrollToBottom();
    });
  });

  function findItemElement(root: HTMLElement, id: string): HTMLElement | null {
    const candidates = Array.from(root.querySelectorAll<HTMLElement>('[data-message-id], [data-timeline-item-id]'));
    for (const candidate of candidates) {
      if (candidate.getAttribute('data-message-id') === id) return candidate;
      if (candidate.getAttribute('data-timeline-item-id') === id) return candidate;
    }
    return null;
  }

  function setScrollTop(node: HTMLElement, top: number, behavior: ScrollBehavior): void {
    if (typeof node.scrollTo === 'function') {
      try {
        node.scrollTo({ top, behavior });
        return;
      } catch {
        // jsdom exposes unimplemented DOM methods in some environments.
      }
    }

    node.scrollTop = top;
  }

  function scrollItemIntoView(
    item: HTMLElement,
    viewport: HTMLElement,
    options: {
      behavior: ScrollBehavior;
      block: ScrollLogicalPosition;
      container: 'all' | 'nearest';
      inline: ScrollLogicalPosition;
    },
  ): void {
    if (typeof item.scrollIntoView === 'function') {
      try {
        item.scrollIntoView(options);
        return;
      } catch {
        // Fall back for DOM environments without the newer container option.
      }
    }

    const viewportTop = viewport.getBoundingClientRect().top;
    const itemTop = item.getBoundingClientRect().top;
    setScrollTop(viewport, viewport.scrollTop + itemTop - viewportTop, options.behavior);
  }

  function getLatestUserMessageId(items: readonly ThreadTimelineItem[]): string | null {
    for (let index = items.length - 1; index >= 0; index -= 1) {
      const item = items[index];
      if (item.type === 'message' && item.message.role === 'user') return item.message.id;
    }
    return null;
  }

  function getCurrentRunId(snapshot: ThreadStateSnapshot | null): string | null {
    return snapshot?.projection.currentRunId
      ?? snapshot?.projection.threadRun?.runtimeRunId
      ?? null;
  }

  function getTimelineScrollSignature(items: readonly ThreadTimelineItem[]): string {
    return items.map((item) => {
      if (item.type === 'message') {
        return [
          item.id,
          item.message.id,
          item.message.role,
          item.message.content.length,
          item.message.streaming ? 'streaming' : 'complete',
        ].join(':');
      }

      if (item.type === 'work') {
        return [
          item.id,
          item.work.id,
          item.work.status,
          item.work.parts.map(getWorkPartSignature).join(','),
        ].join(':');
      }

      if (item.type === 'runtime-request') {
        return [item.id, item.request.id, item.request.kind].join(':');
      }

      return [item.id, item.type].join(':');
    }).join('|');
  }

  function getWorkPartSignature(part: ThreadWorkPart): string {
    if (part.type === 'reasoning') return [part.id, part.type, part.text.length, part.status].join(':');
    if (part.type === 'assistant-draft') {
      return [
        part.id,
        part.type,
        part.message.id,
        part.message.content.length,
        part.message.streaming ? 'streaming' : 'complete',
      ].join(':');
    }
    if (part.type === 'tool') return [part.id, part.type, part.tool.callId, part.tool.status].join(':');
    if (part.type === 'tool-group') return [part.id, part.type, part.group.id, part.group.status].join(':');
    if (part.type === 'warning') return [part.id, part.type, part.message].join(':');
    return [part.id, part.type, part.label].join(':');
  }
</script>

<div {...elementProps} {@attach viewportAttachment}>
  {#if children}
    {@render children({ props: elementProps, timeline, viewport })}
  {:else}
    <ThreadTimeline {thread} {timeline} />
  {/if}

  {#if turnAnchor === 'top' && topAnchorReserveHeight > 0}
    <div
      aria-hidden="true"
      data-hpd-thread-top-anchor-reserve
      style:height={`${topAnchorReserveHeight}px`}
      style:flex-shrink="0"
      style:pointer-events="none"
    ></div>
  {/if}
</div>
