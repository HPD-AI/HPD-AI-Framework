<script lang="ts">
  import {
    ThreadStatus,
    ThreadStatusIndicator,
    ThreadStatusMetrics,
    type ThreadState,
    type ThreadStateSnapshot,
  } from '../src/index.js';
  import type {
    RuntimeRequest,
    ThreadProjectionSnapshot,
    ToolCall,
  } from '@hpd-research/hpd-agent-headless-ui';

  type RenderMode = 'default' | 'metrics' | 'children' | 'child';
  type Scenario = 'ready' | 'loading' | 'error' | 'disconnected' | 'requesting' | 'working';

  let {
    renderMode = 'default',
    scenario = 'working',
  }: {
    renderMode?: RenderMode;
    scenario?: Scenario;
  } = $props();

  const thread = $derived(createStoryThread(createSnapshot(scenario)));

  function createStoryThread(snapshot: ThreadStateSnapshot): ThreadState {
    return {
      controller: {} as ThreadState['controller'],
      subscribe(run) {
        run(snapshot);
        return () => {};
      },
      getSnapshot: () => snapshot,
      clearError: () => {},
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
      answerClientToolRequest: async () => undefined,
    };
  }

  function createSnapshot(mode: Scenario): ThreadStateSnapshot {
    const request = runtimeRequest();
    const tool = toolCall();
    const activeTools = mode === 'working' ? [tool] : [];
    const pendingRuntimeRequests = mode === 'requesting' ? [request] : [];
    const loading = mode === 'loading';
    const connected = mode !== 'disconnected';
    const error = mode === 'error' ? 'The thread failed to load.' : null;
    const streaming = mode === 'working';
    const activity = {
      status: error
        ? 'failed'
        : pendingRuntimeRequests.length > 0
          ? 'requesting'
          : streaming
            ? 'working'
            : 'idle',
      streaming,
      reasoning: false,
      activeToolCount: activeTools.length,
      pendingRequestCount: pendingRuntimeRequests.length,
    } as const;
    const threadExecution = mode === 'working'
      ? {
          threadExecutionId: 'storybook-run',
          agentId: 'agent',
          status: 'active',
        } as const
      : null;
    const projection: ThreadProjectionSnapshot = {
      thread: null,
      timeline: [],
      workGroups: [],
      transcriptMessages: [],
      activeTools,
      pendingRuntimeRequests,
      threadExecution,
      activity,
      currentTurnId: null,
      currentConversationId: null,
      currentExecutionId: threadExecution?.threadExecutionId ?? null,
      error,
      canSend: mode === 'ready',
    };

    return {
      projection,
      timeline: [],
      workGroups: [],
      transcriptMessages: [],
      activity,
      activeTools,
      pendingRuntimeRequests,
      textSubmissionState: mode === 'ready'
        ? { canSubmit: true, reason: null }
        : { canSubmit: false, reason: error ? 'error' : 'busy' },
      canSubmitText: mode === 'ready',
      loading,
      connected,
      error,
    };
  }

  function toolCall(): ToolCall {
    return {
      callId: 'tool-1',
      name: 'SearchDocs',
      messageId: 'message-1',
      status: 'executing',
      startTime: new Date('2026-01-01T00:00:00.000Z'),
    };
  }

  function runtimeRequest(): RuntimeRequest {
    return {
      id: 'perm-1',
      kind: 'permission',
      sourceName: 'PermissionMiddleware',
      requestEventType: 'PERMISSION_REQUEST',
      expectedResponseEventType: 'PERMISSION_RESPONSE',
      request: {
        permissionId: 'perm-1',
        sourceName: 'PermissionMiddleware',
        functionName: 'Bash',
        description: 'Allow npm test.',
        callId: 'call-1',
        arguments: { command: 'npm test' },
      },
    };
  }
</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Headless Svelte adapter</p>
    <h1>ThreadStatus tutorial playground</h1>
    <p>
      `ThreadStatus` reads the current thread snapshot and exposes a small,
      read-only display model.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>What to inspect</h2>
      <ol>
        <li>Switch scenarios to see state priority.</li>
        <li>Compare default, metrics, `children`, and `child` rendering.</li>
        <li>Inspect stable data attributes on the root element.</li>
      </ol>
    </aside>

    <main class="preview">
      {#if renderMode === 'child'}
        <ThreadStatus {thread}>
          {#snippet child(status)}
            <article {...status.props} class="status custom">
              <strong>{status.state}</strong>
              <span>{status.label}</span>
              <small>{status.activeTools.length} active tools</small>
            </article>
          {/snippet}
        </ThreadStatus>
      {:else if renderMode === 'metrics'}
        <ThreadStatus {thread} class="status with-metrics">
          {#snippet children(status)}
            <ThreadStatusIndicator {status} />
            <ThreadStatusMetrics {status} class="metrics" />
          {/snippet}
        </ThreadStatus>
      {:else if renderMode === 'children'}
        <ThreadStatus {thread} class="status">
          {#snippet children(status)}
            <strong>{status.label}</strong>
            <small>{status.state}</small>
          {/snippet}
        </ThreadStatus>
      {:else}
        <ThreadStatus {thread} class="status" />
      {/if}
    </main>
  </div>
</section>

<style>
  .tutorial {
    min-height: 100%;
    padding: 28px;
    background: #f3f6f5;
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
    color: #296c61;
    font-size: 12px;
    font-weight: 700;
  }

  h1, h2, p {
    margin-top: 0;
  }

  .layout {
    display: grid;
    grid-template-columns: minmax(220px, 300px) minmax(0, 1fr);
    gap: 20px;
  }

  .guide,
  .preview {
    border: 1px solid #d3dad5;
    border-radius: 8px;
    background: #fffefb;
  }

  .guide {
    padding: 18px;
  }

  .guide ol {
    padding-left: 18px;
  }

  .guide li {
    font-size: 13px;
  }

  .preview {
    display: grid;
    align-content: start;
    min-height: 280px;
    padding: 18px;
  }

  .status {
    display: inline-grid;
    gap: 6px;
    width: fit-content;
    min-width: 180px;
    padding: 12px 14px;
    border: 1px solid #b9c9c0;
    border-radius: 8px;
    background: #f7fbf8;
  }

  .status[data-status-state='error'] {
    border-color: #d7aaa6;
    background: #fff7f6;
  }

  .status[data-status-state='requesting'] {
    border-color: #cdbf84;
    background: #fffbea;
  }

  .status[data-status-state='working'] {
    border-color: #9fbfd0;
    background: #f2f9fc;
  }

  .status strong {
    font-size: 15px;
  }

  .status small,
  .status span {
    color: #5c655d;
    font-size: 13px;
  }

  :global(.metrics) {
    display: flex;
    gap: 6px;
    flex-wrap: wrap;
  }

  :global(.metrics span) {
    border: 1px solid #d3dad5;
    border-radius: 999px;
    padding: 3px 7px;
    background: #ffffff;
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
