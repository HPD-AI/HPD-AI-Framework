# MarkdownText DX

`MarkdownText` renders assistant text using `@humanspeak/svelte-markdown`.

It is Svelte adapter rendering policy. The client and framework-neutral core
own durable text, events, and projection lifecycle; the Svelte adapter owns
markdown, sanitization, links, KaTeX, Mermaid, and custom renderer snippets.

## Basic Use

```svelte
<MarkdownText text={assistantText} />
```

With a projected message:

```svelte
<MarkdownText {message} />
```

When used by `MessageParts`, assistant text is routed through `MarkdownText`
automatically. User text stays on `DirectiveText`.

## Streaming

`message.content` is the accumulated text. `message.contents` may contain raw
text deltas, but markdown rendering uses the full accumulated string.

```svelte
<MarkdownText {message} streaming={message.streaming} />
```

KaTeX can render while streaming. Mermaid is async, so by default Mermaid is
enabled only after the message is complete.

```svelte
<MarkdownText
  text={assistantText}
  streaming={isStreaming}
  features={{ katex: true, mermaid: true }}
/>
```

## Custom Rendering

```svelte
<MarkdownText text={assistantText}>
  {#snippet code({ lang, text })}
    <CodeBlock {lang} {text} />
  {/snippet}

  {#snippet link({ href, children })}
    <a href={href} target="_blank" rel="noreferrer">
      {@render children?.()}
    </a>
  {/snippet}
</MarkdownText>
```

Use `extensions`, `renderers`, `options`, and `preprocess` when the app needs
lower-level control from `@humanspeak/svelte-markdown`.

## Styling Hooks

`MarkdownText` exposes stable HPD-owned attributes on its root element:

```css
[data-hpd-markdown-text] {
}

[data-hpd-markdown-text][data-streaming] {
}

[data-hpd-markdown-text][data-mermaid-enabled] {
}

[data-hpd-markdown-text][data-message-id] {
}
```

Markdown content renders as normal HTML inside that root, so apps can style the
standard elements directly:

```css
[data-hpd-markdown-text] :where(h1, h2, h3, h4, h5, h6) {
}

[data-hpd-markdown-text] p {
}

[data-hpd-markdown-text] a {
}

[data-hpd-markdown-text] blockquote {
}

[data-hpd-markdown-text] :where(ul, ol, li) {
}

[data-hpd-markdown-text] table {
}

[data-hpd-markdown-text] :where(th, td) {
}

[data-hpd-markdown-text] img {
}

[data-hpd-markdown-text] hr {
}
```

The default code renderer adds a HPD hook to the `<pre>`:

```css
[data-hpd-markdown-text] pre[data-hpd-markdown-code] {
}

[data-hpd-markdown-text] pre[data-hpd-markdown-code] code {
}

[data-hpd-markdown-text] code.language-ts {
}
```

KaTeX and Mermaid expose feature hooks from their renderers:

```css
[data-hpd-markdown-text] .katex {
}

[data-hpd-markdown-text] .katex-display {
  overflow-x: auto;
}

[data-hpd-markdown-text] .mermaid-loading {
}

[data-hpd-markdown-text] .mermaid-error {
}

[data-hpd-markdown-text] .mermaid-diagram {
}
```

Use snippets when styling is not enough. `code`, `link`, `inlineKatex`,
`blockKatex`, and `mermaid` let the app replace the rendered structure while
keeping `MarkdownText` as the markdown policy boundary.
