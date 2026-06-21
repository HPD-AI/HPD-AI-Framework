<script lang="ts">
  import {
    MarkdownText,
    MessageParts,
  } from '../src/index.js';
  import type { Message } from '@hpd-research/hpd-agent-headless-ui';

  type Variant = 'default' | 'streaming' | 'message-parts' | 'custom';

  let {
    variant = 'default',
  }: {
    variant?: Variant;
  } = $props();

  const markdown = `# Assistant answer

This text renders **bold**, _emphasis_, \`inline code\`, links like [HPD](https://example.com), and safe HTML like <kbd>⌘</kbd><kbd>K</kbd>.

> A blockquote can carry quoted model context or a cited passage.

## Checklist

- [x] Render accumulated projected text
- [x] Keep raw text deltas available
- [ ] Decide whether Mermaid should render during active streaming

## Table

| Feature | Streaming | Final render | Notes |
| --- | :---: | :---: | --- |
| Markdown | yes | yes | Incremental rendering |
| KaTeX | yes | yes | Synchronous |
| Mermaid | no | yes | Async by default |

## Math

Inline math: \\(e^{i\\pi} + 1 = 0\\)

Block math:

\\[
\\int_0^1 x^2\\,dx = \\frac{1}{3}
\\]

AMS environment:

\\begin{equation}
E = mc^2
\\end{equation}

## Code

\`\`\`ts
type MarkdownPolicy = {
  katex: boolean;
  mermaid: 'final' | 'streaming';
};
\`\`\`

## Mermaid

\`\`\`mermaid
graph TD
  A[Client TEXT_DELTA] --> B[Headless projection]
  B --> C[message.content]
  C --> D[MarkdownText]
  D --> E{streaming?}
  E -->|yes| F[Markdown + KaTeX]
  E -->|no| G[Markdown + KaTeX + Mermaid]
\`\`\`
`;

  const message = $derived<Message>({
    id: 'assistant-1',
    role: 'assistant',
    content: markdown,
    contents: [
      { $type: 'text', text: markdown.slice(0, 40) },
      { $type: 'text', text: markdown.slice(40) },
    ],
    streaming: variant === 'streaming',
    thinking: false,
    timestamp: new Date('2026-01-01T00:00:00.000Z'),
    toolCalls: [],
    turnId: 'turn-1',
    conversationId: 'conversation-1',
    runId: 'run-1',
    placement: 'transcript',
  });
</script>

<section class="tutorial">
  <header>
    <p class="eyebrow">Assistant text primitive</p>
    <h1>Markdown text</h1>
    <p>
      Assistant text is rendered from accumulated <code>message.content</code>,
      while raw text deltas remain available on <code>message.contents</code>.
    </p>
  </header>

  <div class="layout">
    <aside>
      <h2>Policy</h2>
      <dl>
        <div><dt>variant</dt><dd>{variant}</dd></div>
        <div><dt>streaming</dt><dd>{message.streaming ? 'yes' : 'no'}</dd></div>
        <div><dt>text deltas</dt><dd>{message.contents.length}</dd></div>
      </dl>
      <p>
        KaTeX can render in streaming mode. Mermaid waits for the settled
        message unless the app opts into async Mermaid while streaming.
      </p>
      <p>
        This story intentionally includes headings, tables, task lists,
        blockquotes, code, KaTeX, HTML, and Mermaid in one assistant message.
      </p>
    </aside>

    <main>
      {#if variant === 'message-parts'}
        <MessageParts {message} />
      {:else if variant === 'custom'}
        <MarkdownText text={markdown} features={{ katex: true, mermaid: true }}>
          {#snippet code({ lang, text })}
            <figure class="code-card">
              <figcaption>{lang || 'code'}</figcaption>
              <pre><code>{text}</code></pre>
            </figure>
          {/snippet}
        </MarkdownText>
      {:else}
        <MarkdownText
          {message}
          streaming={message.streaming}
          features={{ katex: true, mermaid: true }}
        />
      {/if}
    </main>
  </div>
</section>

<style>
  .tutorial {
    color: #1f2528;
    display: grid;
    gap: 1.5rem;
    padding: 2rem;
  }

  .eyebrow {
    color: #2b7a68;
    font-size: 0.82rem;
    font-weight: 800;
    letter-spacing: 0;
    margin: 0 0 0.5rem;
    text-transform: uppercase;
  }

  h1 {
    font-size: 2.5rem;
    line-height: 1.05;
    margin: 0 0 1rem;
  }

  h2,
  p {
    margin-top: 0;
  }

  .layout {
    align-items: start;
    display: grid;
    gap: 1.5rem;
    grid-template-columns: minmax(18rem, 24rem) minmax(0, 1fr);
  }

  aside,
  main {
    background: #fff;
    border: 1px solid #d8dde0;
    border-radius: 8px;
    padding: 1.25rem;
  }

  dl {
    display: grid;
    gap: 0.5rem;
  }

  dl div {
    display: flex;
    justify-content: space-between;
    gap: 1rem;
  }

  dt {
    color: #5d686e;
    font-weight: 700;
  }

  .code-card {
    border: 1px solid #cbd3d8;
    border-radius: 8px;
    margin: 1rem 0;
    overflow: hidden;
  }

  .code-card figcaption {
    background: #eef2f4;
    color: #4c5960;
    font-size: 0.8rem;
    font-weight: 700;
    padding: 0.45rem 0.75rem;
  }

  .code-card pre {
    margin: 0;
    overflow: auto;
    padding: 0.75rem;
  }
</style>
