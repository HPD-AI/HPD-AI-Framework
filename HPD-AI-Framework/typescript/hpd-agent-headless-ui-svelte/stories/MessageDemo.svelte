<script lang="ts">
  import type { Message as ThreadMessage } from '@hpd-research/hpd-agent-headless-ui';
  import {
    Message,
    MessageParts,
    Reasoning,
  } from '../src/index.js';

  type RenderMode = 'default' | 'parts' | 'child';
  type Scenario = 'text' | 'structured' | 'working';

  let {
    renderMode = 'default',
    scenario = 'text',
    showActions = true,
  }: {
    renderMode?: RenderMode;
    scenario?: Scenario;
    showActions?: boolean;
  } = $props();

  const message = $derived(createMessage(scenario));

  function createMessage(currentScenario: Scenario): ThreadMessage {
    const base: ThreadMessage = {
      id: 'message-1',
      role: currentScenario === 'text' ? 'user' : 'assistant',
      content: 'Flattened fallback text.',
      contents: [{ $type: 'text', text: 'Plain projected text.' }],
      streaming: false,
      thinking: false,
      timestamp: new Date('2026-01-01T00:00:00.000Z'),
      toolCalls: [],
      turnId: 'turn-1',
      conversationId: 'conversation-1',
      runId: null,
      placement: 'transcript',
    };

    if (currentScenario === 'structured') {
      return {
        ...base,
        content: 'Structured fallback should not be the primary render path.',
        contents: [
          { $type: 'reasoning', text: 'Inspecting the projected content parts.' },
          { $type: 'text', text: 'The message can render structured text.' },
          {
            $type: 'uri',
            uri: 'hpd-content://report-1',
            mediaType: 'text/markdown',
          },
        ],
        reasoning: 'Fallback reasoning.',
        toolCalls: [{
          callId: 'tool-1',
          name: 'read_file',
          messageId: 'message-1',
          status: 'complete',
          startTime: new Date('2026-01-01T00:00:01.000Z'),
          endTime: new Date('2026-01-01T00:00:02.000Z'),
          turnId: 'turn-1',
          conversationId: 'conversation-1',
          runId: null,
        }],
      };
    }

    if (currentScenario === 'working') {
      return {
        ...base,
        role: 'assistant',
        content: '',
        contents: [
          { $type: 'reasoning', text: 'Thinking through the next step.' },
          { $type: 'text', text: 'Drafting the answer as events stream in' },
        ],
        streaming: true,
        thinking: true,
        toolCalls: [{
          callId: 'tool-2',
          name: 'search',
          messageId: 'message-1',
          status: 'executing',
          startTime: new Date('2026-01-01T00:00:01.000Z'),
          turnId: 'turn-1',
          conversationId: 'conversation-1',
          runId: 'run-1',
        }],
      };
    }

    return base;
  }
</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Headless Svelte adapter</p>
    <h1>Message tutorial playground</h1>
    <p>
      `Message` owns the root wrapper and actions. `MessageParts` renders
      structured HPD content.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>What to inspect</h2>
      <ol>
        <li>Text renders from accumulated `message.content`.</li>
        <li>Structured mode keeps non-text `message.contents` as separate parts.</li>
        <li>Custom parts mode switches on `part.type`.</li>
        <li>Working mode adds thinking, reasoning, tool, and cursor parts.</li>
      </ol>
      <dl>
        <div><dt>role</dt><dd>{message.role}</dd></div>
        <div><dt>contents</dt><dd>{message.contents.length}</dd></div>
        <div><dt>tools</dt><dd>{message.toolCalls.length}</dd></div>
        <div><dt>streaming</dt><dd>{message.streaming ? 'yes' : 'no'}</dd></div>
      </dl>
    </aside>

    <main class="preview">
      {#if renderMode === 'child'}
        <Message {message} {showActions} class="message">
          {#snippet child({ props, message, parts, status })}
            <article {...props} class="message custom-root">
              <header>
                <strong>{message.role}</strong>
                <span>{status}</span>
                <small>{parts.length} parts</small>
              </header>
              <MessageParts {message} />
            </article>
          {/snippet}
        </Message>
      {:else if renderMode === 'parts'}
        <Message {message} {showActions} class="message">
          {#snippet children({ message })}
            <MessageParts {message} class="parts custom-parts">
              {#snippet part({ part, props })}
                {#if part.type === 'reasoning'}
                  <Reasoning text={part.text} status={part.status} />
                {:else if part.type === 'text'}
                  <p {...props}>{part.text}</p>
                {:else if part.type === 'content'}
                  <a {...props} href={part.content.$type === 'uri' ? part.content.uri : undefined}>
                    {part.content.$type === 'uri' ? part.content.uri : part.content.$type}
                  </a>
                {:else if part.type === 'tool'}
                  <div {...props}>{part.tool.name}: {part.tool.status}</div>
                {:else if part.type === 'thinking'}
                  <div {...props}>Thinking...</div>
                {:else if part.type === 'cursor'}
                  <span {...props}>|</span>
                {/if}
              {/snippet}
            </MessageParts>
          {/snippet}
        </Message>
      {:else}
        <Message {message} {showActions} class="message" />
      {/if}
    </main>
  </div>
</section>

<style>
  .tutorial {
    min-height: 100%;
    padding: 28px;
    background: #f4f6f8;
    color: #202427;
    font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  .intro {
    max-width: 760px;
    margin-bottom: 24px;
  }

  .eyebrow {
    margin: 0 0 6px;
    color: #ad5b22;
    font-size: 12px;
    font-weight: 800;
    letter-spacing: 0;
    text-transform: uppercase;
  }

  h1, h2, p {
    margin-top: 0;
  }

  .layout {
    display: grid;
    grid-template-columns: minmax(220px, 320px) minmax(0, 1fr);
    gap: 24px;
    align-items: start;
  }

  .guide ol {
    margin: 0 0 20px;
    padding-left: 20px;
  }

  dl {
    display: grid;
    gap: 8px;
    margin: 0;
  }

  dl div {
    display: flex;
    justify-content: space-between;
    gap: 16px;
    border-bottom: 1px solid #d7dde1;
    padding-bottom: 8px;
  }

  dt {
    color: #657177;
    font-weight: 700;
  }

  .preview {
    max-width: 820px;
  }

  .message {
    display: grid;
    gap: 12px;
    border: 1px solid #d0d7dc;
    border-radius: 8px;
    background: #ffffff;
    padding: 16px;
  }

  .custom-root header {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 10px;
    color: #667279;
  }

  :global([data-hpd-message-parts]) {
    display: grid;
    gap: 10px;
  }

  :global([data-hpd-message-part]) {
    margin: 0;
  }

  :global([data-part-type="content"]) {
    color: #8b501f;
    font-weight: 700;
  }

  :global([data-part-type="tool"]) {
    border-left: 3px solid #7996ad;
    padding-left: 10px;
    color: #41505a;
  }

  :global([data-part-type="cursor"]) {
    display: inline-block;
    width: fit-content;
    color: #ad5b22;
  }

  @media (max-width: 760px) {
    .tutorial {
      padding: 18px;
    }

    .layout {
      grid-template-columns: 1fr;
    }
  }
</style>
