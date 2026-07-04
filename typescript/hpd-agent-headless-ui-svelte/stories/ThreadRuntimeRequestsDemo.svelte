<script lang="ts">
  import {
    RuntimeRequest,
    ThreadRuntimeRequests,
    type ThreadState,
    type ThreadStateSnapshot,
  } from '../src/index.js';
  import type {
    RuntimeRequest as RuntimeRequestItem,
    ThreadProjectionSnapshot,
  } from '@hpd-research/hpd-agent-headless-ui';

  type RenderMode = 'default' | 'custom';
  type Scenario = 'mixed' | 'known-only' | 'custom-only' | 'empty';

  let {
    renderMode = 'default',
    scenario = 'mixed',
    useThread = true,
  }: {
    renderMode?: RenderMode;
    scenario?: Scenario;
    useThread?: boolean;
  } = $props();

  let actionLog = $state<string[]>([]);
  const requests = $derived(createRequests(scenario));
  const thread = $derived(createStoryThread(requests));

  function log(message: string) {
    actionLog = [message, ...actionLog].slice(0, 6);
  }

  function createStoryThread(requests: RuntimeRequestItem[]): ThreadState {
    return {
      controller: {} as ThreadState['controller'],
      subscribe(run) {
        run(createSnapshot(requests));
        return () => {};
      },
      getSnapshot: () => createSnapshot(requests),
      clearError: () => {},
      start: async () => {},
      rehydrate: async () => {},
      connect: async () => {},
      disconnect: async () => {},
      dispose: async () => {},
      sendMessage: async () => {},
      run: async () => undefined,
      respond: async (input) => {
        log(`respond ${input.type}`);
        return undefined;
      },
      interrupt: async () => {},
      approve: async (id) => {
        log(`approve ${id}`);
        return undefined;
      },
      deny: async (id, reason) => {
        log(`deny ${id}${reason ? `: ${reason}` : ''}`);
        return undefined;
      },
      clarify: async (id, answer) => {
        log(`clarify ${id}: ${answer}`);
        return undefined;
      },
      answerClientToolRequest: async (id) => {
        log(`client tool ${id}`);
        return undefined;
      },
    };
  }

  function createSnapshot(requests: RuntimeRequestItem[]): ThreadStateSnapshot {
    const activity = {
      status: requests.length > 0 ? 'requesting' : 'idle',
      streaming: false,
      reasoning: false,
      activeToolCount: 0,
      pendingRequestCount: requests.length,
    } as const;
    const projection: ThreadProjectionSnapshot = {
      thread: null,
      timeline: [],
      workGroups: [],
      transcriptMessages: [],
      activeTools: [],
      pendingRuntimeRequests: requests,
      threadRun: null,
      activity,
      currentTurnId: null,
      currentConversationId: null,
      currentRunId: null,
      error: null,
      canSend: requests.length === 0,
    };

    return {
      projection,
      timeline: [],
      workGroups: [],
      transcriptMessages: [],
      activity,
      activeTools: [],
      pendingRuntimeRequests: requests,
      textSubmissionState: requests.length === 0
        ? { canSubmit: true, reason: null }
        : { canSubmit: false, reason: 'busy' },
      canSubmitText: requests.length === 0,
      loading: false,
      connected: true,
      error: null,
    };
  }

  function createRequests(mode: Scenario): RuntimeRequestItem[] {
    const known = [permissionRequest(), clarificationRequest(), clientToolRequest()];
    const custom = [customRequest()];
    if (mode === 'empty') return [];
    if (mode === 'known-only') return known;
    if (mode === 'custom-only') return custom;
    return [...known, ...custom];
  }

  function permissionRequest(): RuntimeRequestItem {
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
        description: 'Run npm test in the project workspace.',
        callId: 'call-1',
        arguments: { command: 'npm test' },
      },
    };
  }

  function clarificationRequest(): RuntimeRequestItem {
    return {
      id: 'clarify-1',
      kind: 'clarification',
      sourceName: 'ClarificationFunction',
      requestEventType: 'CLARIFICATION_REQUEST',
      expectedResponseEventType: 'CLARIFICATION_RESPONSE',
      request: {
        requestId: 'clarify-1',
        sourceName: 'ClarificationFunction',
        question: 'Which tenant should the agent use?',
        options: ['dev', 'prod'],
      },
    };
  }

  function clientToolRequest(): RuntimeRequestItem {
    return {
      id: 'tool-1',
      kind: 'client-tool',
      sourceName: 'HPD.Agent.ClientTools',
      requestEventType: 'CLIENT_TOOL_INVOKE_REQUEST',
      expectedResponseEventType: 'CLIENT_TOOL_INVOKE_OUTCOME',
      responsePolicy: 'targetedResponder',
      visibility: 'allObservers',
      request: {
        requestId: 'tool-1',
        sourceName: 'HPD.Agent.ClientTools',
        toolName: 'pickFile',
        callId: 'call-2',
        description: 'Pick a file from the local client.',
        arguments: { accept: 'image/*' },
      },
    };
  }

  function customRequest(): RuntimeRequestItem {
    return {
      id: 'custom-1',
      kind: 'custom',
      sourceName: 'Custom.Workflow',
      requestEventType: 'CUSTOM_REVIEW_REQUEST',
      expectedResponseEventType: 'CUSTOM_REVIEW_RESPONSE',
      responsePolicy: 'firstValidResponseWins',
      visibility: 'allObservers',
      event: {
        type: 'CUSTOM_REVIEW_REQUEST',
        requestId: 'custom-1',
        sourceName: 'Custom.Workflow',
        prompt: 'Review a custom workflow transition.',
      },
    };
  }
</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Headless Svelte adapter</p>
    <h1>Runtime requests tutorial playground</h1>
    <p>
      `ThreadRuntimeRequests` renders pending request lifecycle state. Known
      requests get typed actions; custom request events stay visible.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>What to inspect</h2>
      <ol>
        <li>Switch scenarios to compare known, custom, and empty states.</li>
        <li>Use custom rendering to see the snippet contract.</li>
        <li>Click actions and inspect the live action log.</li>
      </ol>

      <h2>Action log</h2>
      {#if actionLog.length === 0}
        <p>No actions yet.</p>
      {:else}
        <ul>
          {#each actionLog as item}
            <li>{item}</li>
          {/each}
        </ul>
      {/if}
    </aside>

    <main class="preview">
      {#if renderMode === 'custom'}
        <ThreadRuntimeRequests thread={useThread ? thread : undefined} requests={useThread ? undefined : requests}>
          {#snippet request({ item, actions, props })}
            <article {...props} class="request-card">
              <header>
                <strong>{item.kind}</strong>
                <span>{item.requestEventType}</span>
              </header>
              <p>{item.sourceName}</p>
              <div class="actions">
                {#if item.kind === 'permission'}
                  <button onclick={() => actions.deny('story denied')}>Deny</button>
                  <button onclick={() => actions.approve('ask')}>Allow once</button>
                {:else if item.kind === 'custom'}
                  <button
                    onclick={() => actions.respond({
                      type: item.expectedResponseEventType ?? 'CUSTOM_RESPONSE',
                      requestId: item.id,
                      sourceName: item.sourceName,
                    })}
                  >
                    Respond
                  </button>
                {:else}
                  <RuntimeRequest {item} thread={useThread ? thread : undefined}>
                    {#snippet clarification({ item, actions, props })}
                      <section {...props} class="inline-kind">
                        <p>{'request' in item ? item.request.question : item.requestEventType}</p>
                        <button onclick={() => actions.clarify('storybook')}>Answer storybook</button>
                      </section>
                    {/snippet}

                    {#snippet clientTool({ item, actions, props })}
                      <section {...props} class="inline-kind">
                        <p>{item.sourceName}</p>
                        <button onclick={() => actions.answerClientToolRequest('storybook response')}>
                          Respond from story
                        </button>
                      </section>
                    {/snippet}
                  </RuntimeRequest>
                {/if}
              </div>
            </article>
          {/snippet}

          {#snippet empty()}
            <div class="empty">No pending runtime requests.</div>
          {/snippet}
        </ThreadRuntimeRequests>
      {:else}
        <ThreadRuntimeRequests thread={useThread ? thread : undefined} requests={useThread ? undefined : requests}>
          {#snippet empty()}
            <div class="empty">No pending runtime requests.</div>
          {/snippet}
        </ThreadRuntimeRequests>
      {/if}
    </main>
  </div>
</section>

<style>
  .tutorial {
    min-height: 100%;
    padding: 28px;
    background: #f4f6f3;
    color: #20231f;
    font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  .intro {
    max-width: 980px;
    margin: 0 auto 24px;
  }

  .eyebrow {
    margin: 0 0 6px;
    color: #326b60;
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
    max-width: 980px;
    margin: 0 auto;
  }

  .guide,
  .preview {
    border: 1px solid #d6ded3;
    border-radius: 8px;
    background: #fffdf8;
  }

  .guide {
    padding: 18px;
  }

  .guide ol {
    padding-left: 18px;
  }

  .guide li,
  .guide p {
    font-size: 13px;
  }

  .preview {
    padding: 18px;
  }

  :global([data-hpd-thread-runtime-requests]) {
    display: grid;
    gap: 12px;
  }

  :global([data-hpd-runtime-request]),
  .request-card {
    display: grid;
    gap: 10px;
    padding: 14px;
    border: 1px solid #cbd8cf;
    border-radius: 8px;
    background: #f9fbf7;
  }

  :global([data-hpd-runtime-request-header]),
  .request-card header {
    display: flex;
    justify-content: space-between;
    gap: 12px;
    color: #526159;
    font-size: 12px;
    text-transform: uppercase;
  }

  :global([data-hpd-runtime-request-actions]),
  .actions {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }

  .inline-kind {
    display: grid;
    gap: 8px;
  }

  :global([data-hpd-runtime-request-field]) {
    display: grid;
    gap: 6px;
    color: #5e685f;
    font-size: 13px;
  }

  :global(input),
  :global(textarea),
  :global(button) {
    font: inherit;
  }

  :global(input),
  :global(textarea) {
    border: 1px solid #c7d0c5;
    border-radius: 6px;
    padding: 8px 10px;
  }

  :global(button) {
    border: 1px solid #2f665d;
    border-radius: 6px;
    padding: 7px 11px;
    background: #2f665d;
    color: white;
    cursor: pointer;
  }

  :global(pre) {
    overflow: auto;
    margin: 0;
    padding: 10px;
    border-radius: 6px;
    background: #eef2ec;
  }

  .empty {
    padding: 18px;
    border: 1px dashed #b9c5bb;
    border-radius: 8px;
    color: #5e685f;
  }

  @media (max-width: 760px) {
    .layout {
      grid-template-columns: 1fr;
    }
  }
</style>
