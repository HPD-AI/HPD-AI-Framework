<script lang="ts">
  import {
    DirectiveText,
    MessageParts,
  } from '../src/index.js';
  import type { Message } from '@hpd-research/hpd-agent-headless-ui';

  let {
    variant = 'default',
  }: {
    variant?: 'default' | 'custom' | 'message-parts';
  } = $props();

  const message: Message = {
    id: 'user-1',
    role: 'user',
    content: 'Ask @Workspace to inspect auth with /deep',
    contents: [{ $type: 'text', text: 'Ask @Workspace to inspect auth with /deep' }],
    streaming: false,
    thinking: false,
    timestamp: new Date('2026-01-01T00:00:00.000Z'),
    toolCalls: [],
    turnId: null,
    conversationId: null,
    runId: null,
    placement: 'transcript',
    additionalProperties: {
      directives: [
        {
          id: 'workspace',
          label: 'Workspace',
          text: '@Workspace',
          trigger: '@',
          type: 'tool',
          metadata: { icon: 'folder' },
        },
        {
          id: 'deep',
          label: 'Deep',
          text: '/deep',
          trigger: '/',
          type: 'command',
          metadata: { modelId: 'deep-model' },
        },
      ],
    },
  };
</script>

<section class="tutorial">
  <header>
    <p class="eyebrow">Message text primitive</p>
    <h1>Directive text</h1>
    <p>
      Human-readable message text is rendered against structured
      <code>additionalProperties.directives</code> metadata.
    </p>
  </header>

  <div class="layout">
    <aside>
      <h2>Metadata</h2>
      <pre>{JSON.stringify(message.additionalProperties, null, 2)}</pre>
    </aside>

    <main>
      {#if variant === 'custom'}
        <DirectiveText {message} text={message.content} class="line">
          {#snippet directive({ directive, props })}
            <span {...props} class={`chip ${directive.trigger === '/' ? 'command' : 'mention'}`}>
              <span class="trigger">{directive.trigger}</span>{directive.label}
            </span>
          {/snippet}
        </DirectiveText>
      {:else if variant === 'message-parts'}
        <MessageParts {message} class="line" />
      {:else}
        <DirectiveText {message} text={message.content} class="line" />
      {/if}
    </main>
  </div>
</section>

<style>
  .tutorial {
    color: #171b1f;
    display: grid;
    gap: 1.5rem;
    padding: 2rem;
  }

  .eyebrow {
    color: #2b7a68;
    font-size: 0.82rem;
    font-weight: 700;
    letter-spacing: 0;
    margin: 0 0 0.5rem;
    text-transform: uppercase;
  }

  h1 {
    font-size: 2.5rem;
    line-height: 1.05;
    margin: 0 0 1rem;
  }

  header p:last-child {
    font-size: 1.1rem;
    line-height: 1.45;
    margin: 0;
    max-width: 56rem;
  }

  .layout {
    display: grid;
    gap: 1.5rem;
    grid-template-columns: minmax(18rem, 26rem) minmax(0, 1fr);
  }

  aside,
  main {
    border: 1px solid #d6d0c2;
    border-radius: 8px;
    padding: 1.25rem;
  }

  main {
    align-content: start;
    display: grid;
    font-size: 1.2rem;
    min-height: 12rem;
  }

  h2 {
    font-size: 1rem;
    margin: 0 0 1rem;
  }

  pre {
    font-size: 0.82rem;
    line-height: 1.45;
    margin: 0;
    overflow: auto;
    white-space: pre-wrap;
  }

  .line {
    line-height: 1.8;
  }

  :global([data-hpd-directive-text-chip]),
  .chip {
    align-items: baseline;
    background: #eef6f3;
    border: 1px solid #b9d8cf;
    border-radius: 999px;
    color: #17584c;
    display: inline-flex;
    font-size: 0.92em;
    font-weight: 700;
    gap: 0.1rem;
    line-height: 1;
    padding: 0.28rem 0.5rem;
  }

  .chip.command {
    background: #f6f0e3;
    border-color: #dfc889;
    color: #6a4e00;
  }

  .trigger {
    opacity: 0.72;
  }

  @media (max-width: 760px) {
    .layout {
      grid-template-columns: 1fr;
    }
  }
</style>
