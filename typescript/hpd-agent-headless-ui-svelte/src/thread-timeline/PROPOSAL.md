# Thread Timeline Proposal

`ThreadTimeline` is the Svelte adapter for the core timeline projection. It is
the first conversation-level component after the break from the old flat
message-list model.

## Contract

Inputs:

- `thread?: ThreadState`
- `timeline?: ThreadTimelineItem[]`

Static `timeline` is useful for stories, tests, exports, and callers that own
their own subscription model. `thread` is the normal live adapter path.

Snippets:

- `message`
- `work`
- `runtimeRequest`
- `progress`
- `warning`
- `empty`

Default leaves:

- `Message`
- `ThreadWorkGroup`
- `RuntimeRequest`

## Why This Exists

The UI needs to represent more than transcript text. One agent turn can include
reasoning, draft text, tool calls, tool results, runtime requests, warnings, and
final transcript output. A message-list component hides that structure and makes
collapse/grouping policy hard to control.

The timeline component exposes the structure without owning visual policy.

## Non-Goals

- Do not bring back `ThreadMessages`.
- Do not reconstruct protocol events.
- Do not own `ThreadState`.
- Do not force one tool-call layout.
- Do not hide runtime requests behind a single built-in prompt.

Applications should be able to render whatever they want, wherever they want,
from the typed timeline context.
