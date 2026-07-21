# ThreadTimelineViewport Proposal

## Summary

`ThreadTimelineViewport` is the Svelte-specific viewport primitive family for
HPD Agent thread timelines.

The core owns projected timeline meaning. `ThreadTimeline` routes and renders
timeline items. `ThreadTimelineViewport` owns DOM scroll ergonomics around that
rendering.

This is intentionally not a port of assistant-ui's React store architecture and
not a resurrection of the archived message-list state machine. HPD already has
thread-native timeline state; the Svelte adapter only needs Svelte-native DOM
behavior.

## Files

```text
src/thread-timeline-viewport/
  PROPOSAL.md
  context.ts
  index.ts
  props.ts
  thread-scroll-to-bottom.svelte
  thread-timeline-viewport.svelte
  thread-timeline-viewport-footer.svelte
  top-anchor.ts
  types.ts
```

## Public API

Default:

```svelte
<ThreadTimelineViewport {thread} />
```

Top-anchored turns:

```svelte
<ThreadTimelineViewport
  {thread}
  autoScroll
  turnAnchor="top"
/>
```

Bottom-pinned turns:

```svelte
<ThreadTimelineViewport
  {thread}
  autoScroll
  turnAnchor="bottom"
/>
```

Sticky footer and jump control:

```svelte
<ThreadTimelineViewport {thread}>
  <ThreadTimeline {thread} />

  <ThreadTimelineViewportFooter>
    <ThreadComposer {thread} />
  </ThreadTimelineViewportFooter>

  <ThreadScrollToBottom>
    Jump to latest
  </ThreadScrollToBottom>
</ThreadTimelineViewport>
```

## Props

```ts
export interface ThreadTimelineViewportProps extends DivProps {
  ariaLabel?: string;
  anchorBlock?: ScrollLogicalPosition;
  anchorInline?: ScrollLogicalPosition;
  atBottomThreshold?: number;
  autoScroll?: boolean;
  children?: Snippet<[ThreadTimelineViewportChildProps]>;
  scrollBehavior?: ScrollBehavior;
  scrollContainer?: 'all' | 'nearest';
  scrollToBottomOnInitialize?: boolean;
  scrollToBottomOnExecutionStart?: boolean;
  thread?: ThreadState;
  timeline?: ThreadTimelineItem[];
  topAnchorMessageClamp?: {
    tallerThan?: string;
    visibleHeight?: string;
  };
  turnAnchor?: 'bottom' | 'top';
}
```

Default values:

```ts
ariaLabel = 'Thread timeline'
atBottomThreshold = 48
autoScroll = true
scrollBehavior = 'auto'
scrollContainer = 'nearest'
scrollToBottomOnInitialize = true
scrollToBottomOnExecutionStart = true
topAnchorMessageClamp = { tallerThan: '10em', visibleHeight: '6em' }
turnAnchor = 'top'
```

## Viewport API

```ts
export interface ThreadTimelineViewportApi {
  readonly autoScrollSuppressed: boolean;
  readonly contentInset: number;
  readonly isAtBottom: boolean;
  registerContentInset(id: string, height: number): void;
  unregisterContentInset(id: string): void;
  registerItem(id: string, element: HTMLElement): void;
  unregisterItem(id: string): void;
  scrollToBottom(options?: { behavior?: ScrollBehavior }): void;
  scrollToItem(id: string, options?: {
    behavior?: ScrollBehavior;
    block?: ScrollLogicalPosition;
    container?: 'all' | 'nearest';
    inline?: ScrollLogicalPosition;
  }): void;
}
```

The API is passed to custom `children` snippets and is also provided through
Svelte context for child primitives.

## Behavior

`autoScroll={true}` means the viewport may move while the user is still near the
active turn. If the user scrolls away, `data-auto-scroll-suppressed` is set and
new timeline changes do not yank the viewport.

`turnAnchor="top"` means a new user message anchors near the top of the
viewport. If the message is near the end of the scroll range, the component
adds a temporary reserve element:

```html
<div data-hpd-thread-top-anchor-reserve aria-hidden="true"></div>
```

That reserve creates enough scrollable space for the user message to sit at the
desired top anchor while assistant/work content streams underneath it.

`turnAnchor="bottom"` keeps the viewport pinned to the bottom when the user is
already near the bottom.

`ThreadTimelineViewportFooter` registers its measured height as a content inset.
`ThreadScrollToBottom` consumes viewport context and disables itself when the
viewport is already at the bottom.

## Svelte 5 Notes

Use:

- `{@attach ...}` for DOM lifecycle;
- snippets for custom rendering;
- callback props and generated prop helpers;
- local `$state`, `$derived`, and `$effect`;
- context only for descendant viewport primitives.

Avoid:

- legacy event dispatchers;
- legacy slots;
- client/core scroll APIs;
- React-like runtime store layers;
- overloading auto-scroll with named anchoring modes.

## Boundary

No lower-layer changes belong here. The client and framework-neutral core
provide timeline data and message ids. The Svelte adapter owns pixels,
listeners, scroll anchoring, and sticky layout ergonomics.
