# MarkdownText Proposal

`MarkdownText` renders assistant text with Svelte-native markdown policy.

## Boundary

Markdown rendering is adapter policy, not protocol:

- client and core keep text and streaming lifecycle
- Svelte renders markdown, sanitization, links, math, diagrams, and custom tags
- apps can override renderers/snippets without changing HPD projections

## Text Source

`MessageParts` renders one text part from `message.content`, not one part per
text delta. Delta content remains available on `message.contents`, but visual
markdown needs the accumulated string.

## Streaming Policy

`@humanspeak/svelte-markdown` handles streaming-safe incremental rendering.
KaTeX is synchronous and can stay enabled while text streams. Mermaid is async,
so the default is:

- streaming message: render markdown/KaTeX, leave Mermaid fences as code
- completed message: enable Mermaid rendering

Apps can opt into Mermaid while streaming with `renderWhileStreaming`.

## Repair

`streamingRepair` is intentionally exposed, but the first implementation is a
no-op. The underlying renderer streams safely; HPD can add tail repair later
without changing the public primitive shape.
