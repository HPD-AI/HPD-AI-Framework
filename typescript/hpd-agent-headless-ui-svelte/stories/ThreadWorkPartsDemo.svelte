<script lang="ts">
  import {
    Reasoning,
    ThreadWorkParts,
  } from '../src/index.js';
  import type {
    ThreadWorkGroup,
    ToolCall,
  } from '@hpd-research/hpd-agent-headless-ui';

  type RenderMode = 'default' | 'custom';

  let {
    renderMode = 'custom',
    showFinalDraft = false,
  }: {
    renderMode?: RenderMode;
    showFinalDraft?: boolean;
  } = $props();

  const work = $derived(createWorkGroup());

  function createWorkGroup(): ThreadWorkGroup {
    return {
      id: 'work-parts-1',
      turnId: 'turn-1',
      conversationId: 'conversation-1',
      executionId: 'run-1',
      status: 'working',
      label: 'Inspecting workspace',
      openByDefault: true,
      finalMessageId: 'draft-final',
      parts: [
        {
          type: 'reasoning',
          id: 'reasoning-1',
          messageId: 'draft-1',
          text: 'Reading the current timeline projection before changing the renderer.',
          status: 'streaming',
        },
        {
          type: 'tool',
          id: 'tool-1',
          tool: createToolCall('rg', 'complete', 'Found the inline work renderer.'),
        },
        {
          type: 'tool',
          id: 'tool-2',
          tool: createToolCall('svelte-check', 'executing'),
        },
        {
          type: 'warning',
          id: 'warning-1',
          message: 'This is demo data showing a projected warning part.',
        },
      ],
    };
  }

  function createToolCall(
    name: string,
    status: ToolCall['status'],
    resultText?: string,
  ): ToolCall {
    return {
      callId: `call-${name}`,
      name,
      messageId: 'draft-1',
      status,
      startTime: new Date('2026-01-01T00:00:01.000Z'),
      args: { target: 'thread-work-group' },
      resultText,
      turnId: 'turn-1',
      conversationId: 'conversation-1',
      executionId: 'run-1',
    };
  }
</script>

<section class="demo">
  <header>
    <div>
      <h1>ThreadWorkParts</h1>
      <p>Structured HPD work lifecycle parts without the surrounding timeline shell.</p>
    </div>
    <span>{renderMode}</span>
  </header>

  <div class="work-shell">
    <div class="summary">
      <strong>{work.label}</strong>
      <span>{work.status}</span>
    </div>

    {#if renderMode === 'custom'}
      <ThreadWorkParts {work} {showFinalDraft} class="parts">
        {#snippet workPart({ part, props })}
          <section {...props} class="part custom-part">
            {#if part.type === 'reasoning'}
              <Reasoning text={part.text} status={part.status} />
            {:else if part.type === 'tool'}
              <strong>{part.tool.name}</strong>
              <span>{part.tool.status}</span>
              {#if part.tool.resultText}
                <p>{part.tool.resultText}</p>
              {/if}
            {:else if part.type === 'warning'}
              <strong>Warning</strong>
              <p>{part.message}</p>
            {:else}
              <strong>{part.type}</strong>
            {/if}
          </section>
        {/snippet}
      </ThreadWorkParts>
    {:else}
      <ThreadWorkParts {work} {showFinalDraft} class="parts" />
    {/if}
  </div>
</section>

<style>
  .demo {
    min-height: 420px;
    padding: 28px;
    background: #f6f7f9;
    color: #111827;
    font-family:
      Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  header {
    display: flex;
    justify-content: space-between;
    gap: 20px;
    margin-bottom: 24px;
  }

  h1 {
    margin: 0;
    font-size: 28px;
    line-height: 1.15;
  }

  p {
    margin: 8px 0 0;
    color: #536070;
  }

  header span {
    align-self: flex-start;
    border: 1px solid #cfd6df;
    border-radius: 6px;
    background: #ffffff;
    padding: 6px 8px;
    font-size: 13px;
  }

  .work-shell {
    max-width: 780px;
    border: 1px solid #d4dae3;
    border-radius: 8px;
    background: #ffffff;
    padding: 16px;
  }

  .summary {
    display: flex;
    justify-content: space-between;
    gap: 16px;
    margin-bottom: 14px;
  }

  .summary span {
    color: #6b7280;
  }

  :global(.parts) {
    display: grid;
    gap: 10px;
  }

  .part {
    border: 1px solid #e1e6ee;
    border-radius: 6px;
    background: #fbfcfe;
    padding: 12px;
  }

  .part strong {
    display: block;
    margin-bottom: 4px;
  }

  .part p {
    margin: 6px 0 0;
    color: #374151;
  }
</style>
