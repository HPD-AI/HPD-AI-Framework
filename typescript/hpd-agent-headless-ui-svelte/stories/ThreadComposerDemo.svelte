<script lang="ts">
  import {
    ThreadComposer,
    type ThreadComposerAutosizeStrategy,
    type ThreadState,
    type ThreadStateSnapshot,
  } from '../src/index.js';
  import type { RuntimeRequest } from '@hpd-research/hpd-agent-headless-ui';

  type RenderMode = 'default' | 'child';
  type SubmissionMode = 'ready' | 'busy' | 'requesting' | 'disabled';
  type AutosizeMode = false | 'pretext' | 'custom';

  let {
    renderMode = 'default',
    submissionMode = 'ready',
    autosize = 'pretext',
    minRows = 1,
    maxRows = 6,
    clear = 'on-submit',
    submitMode = 'enter',
    initialValue = '',
    showRef = true,
  }: {
    renderMode?: RenderMode;
    submissionMode?: SubmissionMode;
    autosize?: AutosizeMode;
    minRows?: number;
    maxRows?: number;
    clear?: 'on-submit' | 'never';
    submitMode?: 'enter' | 'mod-enter' | 'none';
    initialValue?: string;
    showRef?: boolean;
  } = $props();

  let value = $state('');
  let textareaRef = $state<HTMLTextAreaElement | null>(null);
  let submissions = $state<string[]>([]);
  let interrupts = $state(0);

  $effect(() => {
    value = initialValue;
  });

  const disabled = $derived(submissionMode === 'disabled');
  const thread = $derived(createStoryThread({
    busy: submissionMode === 'busy',
    requesting: submissionMode === 'requesting',
    onSend(text) {
      submissions = [text, ...submissions].slice(0, 5);
    },
    onInterrupt() {
      interrupts += 1;
    },
  }));
  const autosizeStrategy = $derived<ThreadComposerAutosizeStrategy>(
    autosize === 'custom'
      ? ({ maxRows, metrics }) =>
          maxRows * metrics.lineHeight + metrics.paddingBlock + metrics.borderBlock
      : autosize,
  );

  function createStoryThread(options: {
    busy: boolean;
    requesting: boolean;
    onInterrupt(): void;
    onSend(text: string): void;
  }): ThreadState {
    const pendingRuntimeRequests = options.requesting ? [createRuntimeRequest()] : [];
    const activity = {
      status: options.requesting
        ? 'requesting' as const
        : options.busy
          ? 'working' as const
          : 'idle' as const,
      streaming: options.busy,
      reasoning: false,
      activeToolCount: 0,
      pendingRequestCount: pendingRuntimeRequests.length,
    };
    const snapshot: ThreadStateSnapshot = {
      projection: {
        thread: null,
        timeline: [],
        workGroups: [],
        transcriptMessages: [],
        activeTools: [],
        pendingRuntimeRequests,
        threadExecution: options.busy
          ? {
              threadExecutionId: 'storybook-run',
              agentId: 'agent',
              status: 'active',
            }
          : null,
        activity,
        currentTurnId: null,
        currentConversationId: null,
        currentExecutionId: options.busy ? 'storybook-run' : null,
        error: null,
        canSend: true,
      },
      timeline: [],
      workGroups: [],
      transcriptMessages: [],
      activity,
      activeTools: [],
      pendingRuntimeRequests,
      textSubmissionState: options.requesting
        ? { canSubmit: false, reason: 'runtime-request' }
        : options.busy
          ? { canSubmit: false, reason: 'busy' }
          : { canSubmit: true, reason: null },
      canSubmitText: !options.busy && !options.requesting,
      loading: false,
      connected: true,
      error: null,
    };

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
      sendMessage: async (input) => {
        options.onSend(input.text ?? `${input.attachments?.length ?? 0} attachment(s)`);
      },
      run: async () => undefined,
      respond: async () => undefined,
      interrupt: async () => {
        options.onInterrupt();
      },
      approve: async () => undefined,
      deny: async () => undefined,
      clarify: async () => undefined,
      answerClientToolRequest: async () => undefined,
    };
  }

  function createRuntimeRequest(): RuntimeRequest {
    return {
      id: 'request-1',
      kind: 'permission',
      sourceName: 'PermissionMiddleware',
      requestEventType: 'PERMISSION_REQUEST',
      expectedResponseEventType: 'PERMISSION_RESPONSE',
      request: {
        permissionId: 'request-1',
        sourceName: 'PermissionMiddleware',
        functionName: 'Bash',
        callId: 'call-1',
        description: 'Allow the pending operation before sending more text.',
      },
    };
  }
</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Headless Svelte adapter</p>
    <h1>ThreadComposer tutorial playground</h1>
    <p>
      `ThreadComposer` owns input behavior and thread submission. You own the DOM,
      styling, and layout.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>What to inspect</h2>
      <ol>
        <li>Switch between default and `child` rendering.</li>
        <li>Toggle busy, requesting, and disabled states to see submit blocking.</li>
        <li>Use requesting to see the "answer request first" admission state.</li>
        <li>Try Enter and Shift+Enter in the textarea.</li>
        <li>Compare Pretext autosize, custom autosize, and disabled autosize.</li>
      </ol>

      <h2>Live state</h2>
      <dl>
        <div><dt>value</dt><dd>{value || 'empty'}</dd></div>
        <div><dt>textareaRef</dt><dd>{showRef ? (textareaRef ? 'attached' : 'null') : 'hidden'}</dd></div>
        <div><dt>interrupts</dt><dd>{interrupts}</dd></div>
      </dl>
    </aside>

    <div class="preview">
      {#if renderMode === 'child'}
        <ThreadComposer
          {thread}
          bind:value
          bind:textareaRef
          autosize={autosizeStrategy}
          {minRows}
          {maxRows}
          {clear}
          {submitMode}
          {disabled}
          pretext={{ font: '16px Inter', lineHeight: 22 }}
        >
          {#snippet child({ state, props })}
            <form {...props.root} class="composer custom">
              <textarea {...props.input} {@attach props.inputAttachment}></textarea>
              <div class="actions">
                <span>{state.blockedReason ?? 'ready'}</span>
                <button {...props.interrupt}>Interrupt</button>
                <button {...props.submit}>Send</button>
              </div>
            </form>
          {/snippet}
        </ThreadComposer>
      {:else}
        <ThreadComposer
          {thread}
          bind:value
          bind:textareaRef
          autosize={autosizeStrategy}
          {minRows}
          {maxRows}
          {clear}
          {submitMode}
          {disabled}
          pretext={{ font: '16px Inter', lineHeight: 22 }}
          class="composer"
        />
      {/if}

      <section class="submitted">
        <h2>Submitted text</h2>
        {#if submissions.length === 0}
          <p>No submissions yet.</p>
        {:else}
          <ul>
            {#each submissions as submission}
              <li>{submission}</li>
            {/each}
          </ul>
        {/if}
      </section>
    </div>
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
    max-width: 880px;
    margin: 0 auto 24px;
  }

  .eyebrow {
    margin: 0 0 6px;
    color: #336b5f;
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

  .guide {
    border-right: 1px solid #d6d9d2;
    padding-right: 18px;
    font-size: 14px;
  }

  .guide ol {
    padding-left: 20px;
  }

  dl div {
    display: grid;
    grid-template-columns: 92px minmax(0, 1fr);
    gap: 8px;
    padding: 6px 0;
    border-top: 1px solid #dfe3dd;
  }

  dt {
    color: #5c6258;
    font-weight: 700;
  }

  dd {
    margin: 0;
    overflow-wrap: anywhere;
  }

  .preview {
    display: grid;
    gap: 16px;
    align-content: start;
  }

  :global([data-hpd-thread-composer].composer),
  .composer {
    display: grid;
    gap: 10px;
    max-width: 680px;
  }

  :global([data-hpd-thread-composer-textarea]) {
    width: 100%;
    box-sizing: border-box;
    resize: none;
    overflow: auto;
    border: 1px solid #aeb7ad;
    border-radius: 8px;
    padding: 10px 12px;
    background: #fff;
    color: #20231f;
    font: 16px/22px Inter, Arial, sans-serif;
  }

  :global([data-hpd-thread-composer-textarea]:focus) {
    outline: 2px solid #5b8d7e;
    outline-offset: 2px;
  }

  .actions,
  :global([data-hpd-thread-composer].composer) {
    align-items: end;
  }

  .actions {
    display: flex;
    gap: 8px;
    justify-content: flex-end;
  }

  :global([data-hpd-thread-composer-submit]),
  :global([data-hpd-thread-composer-interrupt]) {
    border: 1px solid #879184;
    border-radius: 6px;
    padding: 8px 12px;
    background: #fefefe;
    color: #20231f;
    font-weight: 700;
  }

  :global([data-hpd-thread-composer-submit]:disabled),
  :global([data-hpd-thread-composer-interrupt]:disabled) {
    opacity: 0.45;
  }

  .submitted {
    max-width: 680px;
    border-top: 1px solid #d6d9d2;
    padding-top: 14px;
  }

  .submitted ul {
    margin: 0;
    padding-left: 20px;
  }

  @media (max-width: 760px) {
    .layout {
      grid-template-columns: 1fr;
    }

    .guide {
      border-right: 0;
      border-bottom: 1px solid #d6d9d2;
      padding: 0 0 14px;
    }
  }
</style>
