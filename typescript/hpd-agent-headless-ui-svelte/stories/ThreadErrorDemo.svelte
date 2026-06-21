<script lang="ts">
  import {
    ThreadError,
    type ThreadState,
    type ThreadStateSnapshot,
  } from '../src/index.js';
  import type {
    ThreadActivity,
    ThreadProjectionSnapshot,
    ThreadWorkGroup,
    ToolCall,
  } from '@hpd-research/hpd-agent-headless-ui';

  type RenderMode = 'default' | 'children' | 'child';
  type Scenario = 'thread' | 'run' | 'work' | 'tool' | 'multiple' | 'none';

  let {
    renderMode = 'default',
    scenario = 'thread',
    showAll = false,
  }: {
    renderMode?: RenderMode;
    scenario?: Scenario;
    showAll?: boolean;
  } = $props();

  const snapshot = $derived(createSnapshot(scenario));
  const thread = $derived(createStoryThread(snapshot));

  function createStoryThread(initialSnapshot: ThreadStateSnapshot): ThreadState {
    let current = $state(initialSnapshot);

    return {
      controller: {} as ThreadState['controller'],
      subscribe(run) {
        run(current);
        return () => {};
      },
      getSnapshot: () => current,
      clearError: () => {
        current = createSnapshot('none');
      },
      start: async () => {},
      rehydrate: async () => {},
      connect: async () => {},
      disconnect: async () => {},
      dispose: async () => {},
      sendMessage: async () => {},
      run: async () => undefined,
      respond: async () => undefined,
      interrupt: async () => {},
      approve: async () => undefined,
      deny: async () => undefined,
      clarify: async () => undefined,
      respondToClientTool: async () => undefined,
    };
  }

  function createSnapshot(mode: Scenario): ThreadStateSnapshot {
    const activeTools = mode === 'tool' || mode === 'multiple' ? [toolCall()] : [];
    const workGroups = mode === 'work' || mode === 'multiple' ? [workGroup()] : [];
    const threadRun = mode === 'run' || mode === 'multiple'
      ? {
          runtimeRunId: 'run-failed',
          agentId: 'agent',
          status: 'failed',
          errorType: 'provider',
          errorMessage: 'The provider rejected the model request.',
        } as const
      : null;
    const error = mode === 'thread' || mode === 'multiple'
      ? 'The stream disconnected before the turn completed.'
      : null;
    const activity: ThreadActivity = {
      status: error || threadRun || workGroups.length > 0 || activeTools.length > 0 ? 'failed' : 'idle',
      streaming: false,
      reasoning: false,
      activeToolCount: activeTools.length,
      pendingRequestCount: 0,
    };
    const projection: ThreadProjectionSnapshot = {
      thread: null,
      timeline: [],
      workGroups,
      transcriptMessages: [],
      activeTools,
      pendingRuntimeRequests: [],
      threadRun,
      activity,
      currentTurnId: 'turn-1',
      currentConversationId: 'conversation-1',
      currentRunId: threadRun?.runtimeRunId ?? 'run-1',
      error,
      canSend: mode === 'none',
    };

    return {
      projection,
      timeline: [],
      workGroups,
      transcriptMessages: [],
      activity,
      activeTools,
      pendingRuntimeRequests: [],
      textSubmissionState: mode === 'none'
        ? { canSubmit: true, reason: null }
        : { canSubmit: false, reason: 'error' },
      canSubmitText: mode === 'none',
      loading: false,
      connected: true,
      error,
    };
  }

  function workGroup(): ThreadWorkGroup {
    return {
      id: 'work-1',
      turnId: 'turn-1',
      conversationId: 'conversation-1',
      runId: 'run-1',
      status: 'failed',
      label: 'Tool planning',
      openByDefault: true,
      parts: [],
      error: 'The work group failed while preparing tool calls.',
    };
  }

  function toolCall(): ToolCall {
    return {
      callId: 'call-1',
      name: 'ReadFile',
      messageId: 'message-1',
      status: 'error',
      startTime: new Date('2026-01-01T00:00:00.000Z'),
      error: 'The requested file is outside the workspace.',
      turnId: 'turn-1',
      conversationId: 'conversation-1',
      runId: 'run-1',
    };
  }
</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Headless Svelte adapter</p>
    <h1>ThreadError tutorial playground</h1>
    <p>
      `ThreadError` renders the normalized error state from one thread snapshot.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>What to inspect</h2>
      <ol>
        <li>Switch scenarios to compare controller, run, work, and tool errors.</li>
        <li>Enable `showAll` to see the default multi-error list.</li>
        <li>Compare snippet modes for wrapper-level and full DOM control.</li>
      </ol>
    </aside>

    <main class="preview">
      {#if renderMode === 'child'}
        <ThreadError {thread} {showAll}>
          {#snippet child(model)}
            {#if model.hasError}
              <article {...model.props.root} class="error-card custom">
                <span>{model.error?.kind}</span>
                <strong>{model.label}</strong>
                <button {...model.props.clearButton} onclick={model.actions.clear}>
                  Clear
                </button>
              </article>
            {/if}
          {/snippet}
        </ThreadError>
      {:else if renderMode === 'children'}
        <ThreadError {thread} {showAll} class="error-card">
          {#snippet children(model)}
            <strong>{model.error?.kind}</strong>
            <p>{model.label}</p>
            <small>{model.errors.length} normalized error(s)</small>
          {/snippet}
        </ThreadError>
      {:else}
        <ThreadError {thread} {showAll} class="error-card" />
      {/if}

      {#if scenario === 'none'}
        <div class="empty">No error is rendered for this snapshot.</div>
      {/if}
    </main>
  </div>
</section>

<style>
  .tutorial {
    min-height: 100%;
    padding: 28px;
    background: #f5f6f2;
    color: #20231f;
    font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  .intro,
  .layout {
    max-width: 920px;
    margin: 0 auto;
  }

  .intro {
    margin-bottom: 24px;
  }

  .eyebrow {
    margin: 0 0 6px;
    color: #7a4f16;
    font-size: 12px;
    font-weight: 700;
  }

  h1,
  h2,
  p {
    margin-top: 0;
  }

  .layout {
    display: grid;
    grid-template-columns: minmax(220px, 300px) minmax(0, 1fr);
    gap: 18px;
    align-items: start;
  }

  .guide,
  .preview {
    border: 1px solid #d9d6ce;
    background: #fffdfa;
  }

  .guide {
    padding: 18px;
  }

  .guide ol {
    margin: 0;
    padding-left: 20px;
  }

  .guide li + li {
    margin-top: 10px;
  }

  .preview {
    min-height: 240px;
    padding: 22px;
  }

  .error-card {
    display: grid;
    gap: 12px;
    padding: 16px;
    border: 1px solid #a84a3a;
    background: #fff4ef;
    color: #3d1d17;
  }

  .error-card :global(button),
  .error-card button {
    width: fit-content;
    border: 1px solid #a84a3a;
    background: #fffdfa;
    color: #3d1d17;
    padding: 8px 12px;
    font: inherit;
    cursor: pointer;
  }

  .custom span {
    width: fit-content;
    padding: 4px 8px;
    background: #3d1d17;
    color: #fffdfa;
    font-size: 12px;
    text-transform: uppercase;
  }

  .empty {
    padding: 16px;
    border: 1px dashed #b7b1a7;
    color: #69635c;
  }
</style>
