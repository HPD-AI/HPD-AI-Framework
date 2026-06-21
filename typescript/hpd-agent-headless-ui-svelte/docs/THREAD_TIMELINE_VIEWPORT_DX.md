# ThreadTimelineViewport DX

`ThreadTimelineViewport` is the Svelte-only scroll container for a projected
thread timeline. It does not project events, reconstruct messages, choose fork
groups, or own a thread runtime. It wraps `ThreadTimeline` by default and owns
DOM behavior that belongs in the framework adapter.

```svelte
<ThreadTimelineViewport {thread} />
```

Use it when a conversation surface needs chat-style scroll ergonomics:

- stay pinned while work streams;
- anchor a new user turn near the top while the assistant streams below it;
- stop autoscrolling when the user scrolls away;
- expose child primitives for sticky footers and jump-to-latest controls.

## Scroll Policy

```svelte
<ThreadTimelineViewport
  {thread}
  autoScroll
  turnAnchor="top"
  scrollBehavior="auto"
  atBottomThreshold={48}
/>
```

`autoScroll={true}` follows new timeline content while the user is still near
the active turn. `autoScroll={false}` disables automatic movement while keeping
the imperative viewport API available.

`turnAnchor="top"` anchors a newly sent user message near the top of the
viewport, inserting a temporary `data-hpd-thread-top-anchor-reserve` element
when there is not enough content below the message yet. The reserve shrinks away
as assistant/work content grows.

`turnAnchor="bottom"` keeps new content pinned to the bottom when the user is
already near the bottom.

Tall user messages can be clamped for anchoring:

```svelte
<ThreadTimelineViewport
  {thread}
  topAnchorMessageClamp={{
    tallerThan: '10em',
    visibleHeight: '6em',
  }}
/>
```

## Child Primitives

`ThreadTimelineViewport` provides viewport context to descendants.

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

`ThreadTimelineViewportFooter` measures its height and registers it as a content
inset so bottom scrolling can account for sticky composer/footer layouts.

`ThreadScrollToBottom` calls `viewport.scrollToBottom()` and disables itself
when the viewport is already at the bottom.

## Custom Rendering

The component renders `ThreadTimeline` by default. Use the `children` snippet to
own the internal DOM while keeping the viewport behavior.

```svelte
<ThreadTimelineViewport {thread}>
  {#snippet children({ timeline, viewport })}
    {#each timeline as item (item.id)}
      {@const anchorId = item.type === 'message' ? item.message.id : item.id}
      <article
        data-timeline-item-id={anchorId}
        {@attach (node) => {
          viewport.registerItem(anchorId, node);
          return () => viewport.unregisterItem(anchorId);
        }}
      >
        {item.type}
      </article>
    {/each}
  {/snippet}
</ThreadTimelineViewport>
```

`viewport` exposes:

- `isAtBottom`
- `autoScrollSuppressed`
- `contentInset`
- `scrollToBottom({ behavior })`
- `scrollToItem(id, { behavior, block, inline, container })`
- `registerItem(id, element)`
- `unregisterItem(id)`
- `registerContentInset(id, height)`
- `unregisterContentInset(id)`

## Boundary

Keep this component family in the Svelte adapter. React, Solid, Vue, or other
adapters should implement their own viewport mechanics over the same core
timeline types. The framework-neutral package stays DOM-free.

## Styling Hooks

Viewport primitives expose stable HPD-owned attributes:

```css
[data-hpd-thread-timeline-viewport] {
}

[data-hpd-thread-timeline-viewport][data-at-bottom] {
}

[data-hpd-thread-timeline-viewport][data-auto-scroll-suppressed] {
}

[data-hpd-thread-timeline-viewport][data-empty] {
}

[data-hpd-thread-timeline-viewport][data-auto-scroll="false"] {
}

[data-hpd-thread-timeline-viewport][data-turn-anchor="top"] {
}

[data-hpd-thread-timeline-viewport][data-turn-anchor="bottom"] {
}

[data-hpd-thread-top-anchor-reserve] {
}

[data-hpd-thread-timeline-viewport-footer] {
}

[data-hpd-thread-scroll-to-bottom] {
}

[data-hpd-thread-scroll-to-bottom][data-at-bottom] {
}
```

Use the `children` snippet when the app wants to own the internal timeline DOM
while keeping viewport registration, top anchoring, bottom scrolling, and footer
inset behavior.
