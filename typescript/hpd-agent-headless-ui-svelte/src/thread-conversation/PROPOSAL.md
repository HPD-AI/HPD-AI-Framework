# Thread Conversation Proposal

`ThreadConversation` is the default one-thread chat shell. It composes the
existing HPD primitives without owning sessions, forks, workspaces, or protocol
state.

## Shape

```text
ThreadConversation
  ThreadStatus
  ThreadTimelineViewport
    ThreadTimeline runtime requests excluded by default
    ThreadRuntimeRequests composer panel by default
    ThreadTimelineViewportFooter
      ThreadScrollToBottom
      ThreadComposer
```

Runtime request placement is explicit:

- `composer-panel` renders pending requests near the composer and excludes
  runtime request timeline items from the default timeline.
- `timeline` renders runtime request items inline and skips the request panel.
- `none` leaves runtime request rendering to app code.

## Boundary

Do not put these responsibilities here:

- active session selection
- thread list navigation
- branch/fork policy
- revision client creation
- protocol event reconstruction
- automatic tool execution
- app modal policy

Those remain app-owned or belong to smaller primitives.

## Customization

Every major region is replaceable through snippets:

- `header`
- `viewport`
- `timeline`
- `requests`
- `footer`
- `composer`
- `child`
- `children`

The default should be boring and correct. Product surfaces can replace regions
without rebuilding the thread lifecycle from scratch.
