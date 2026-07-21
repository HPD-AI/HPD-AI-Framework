<script lang="ts">
  import { untrack } from 'svelte';
  import {
    canEditMessage,
    canRetryMessage,
    createThreadRevisionState,
    Message,
    MessageEdit,
    ThreadConversation,
    ThreadComposer,
    ThreadRuntimeRequests,
    ThreadScrollToBottom,
    ThreadStatus,
    ThreadTimeline,
    type ThreadComposerAutosizeStrategy,
    type ThreadState,
    type ThreadStateSnapshot,
  } from '../src/index.js';
  import type {
    Message as ThreadMessage,
    RuntimeRequest,
  } from '@hpd-research/hpd-agent-headless-ui';
  import type {
    AgentClient,
    AgentRunInputEvent,
    Thread as ClientThread,
    ThreadMessage as ClientThreadMessage,
  } from '@hpd-research/hpd-agent-client';

  type RenderMode = 'shell' | 'custom';
  type Scenario = 'starter' | 'empty' | 'busy';
  type RequestScenario = 'permission' | 'clarification' | 'client-tool' | 'custom' | 'mixed';
  type AutosizeMode = false | 'pretext' | 'custom';

  interface StoryBranch {
    id: string;
    label: string;
    parentId: string | null;
    reason: string;
    messages: ThreadMessage[];
  }

  let {
    renderMode = 'shell',
    scenario = 'starter',
    reverse = false,
    autosize = 'pretext',
    submitMode = 'enter',
    clear = 'on-submit',
    showRunConfig = true,
    triggerRuntimeRequests = false,
    requestScenario = 'mixed',
  }: {
    renderMode?: RenderMode;
    scenario?: Scenario;
    reverse?: boolean;
    autosize?: AutosizeMode;
    submitMode?: 'enter' | 'mod-enter' | 'none';
    clear?: 'on-submit' | 'never';
    showRunConfig?: boolean;
    triggerRuntimeRequests?: boolean;
    requestScenario?: RequestScenario;
  } = $props();

  let value = $state('');
  let modelId = $state('fast');
  let skipTools = $state(false);
  let submittedCount = $state(0);
  let interruptedCount = $state(0);
  let lastRunConfig = $state('none');
  let branches = $state<StoryBranch[]>([]);
  let requestLog = $state<string[]>([]);
  let messageActionLog = $state<string[]>([]);
  let resolvedRequestIds = $state<string[]>([]);
  let pendingRuntimeRequests = $state<RuntimeRequest[]>([]);
  let activeThreadId = $state('main');
  let revisionCount = $state(0);
  const subscribers = new Set<(snapshot: ThreadStateSnapshot) => void>();

  const activeBranch = $derived(
    branches.find((branch) => branch.id === activeThreadId) ?? branches[0] ?? createEmptyBranch(),
  );
  const messages = $derived(activeBranch.messages);
  const busy = $derived(scenario === 'busy' || pendingRuntimeRequests.length > 0);
  const autosizeStrategy = $derived<ThreadComposerAutosizeStrategy>(
    autosize === 'custom'
      ? ({ minRows, metrics }) =>
          minRows * metrics.lineHeight + metrics.paddingBlock + metrics.borderBlock
      : autosize,
  );
  const thread = createStoryThread();
  const revisions = createThreadRevisionState({
    client: createRevisionClient(),
    agentId: 'agent',
    sessionId: 's1',
    threadId: 'main',
    onRevisionCreated: (result) => {
      activeThreadId = result.threadId;
      messageActionLog = [
        `${result.kind} created ${result.threadId} from ${result.inputMessageId}`,
        ...messageActionLog,
      ].slice(0, 5);
      notify();
    },
    onError: (error) => {
      messageActionLog = [`revision failed: ${error.message}`, ...messageActionLog].slice(0, 5);
      notify();
    },
  });

  $effect(() => {
    triggerRuntimeRequests;
    requestScenario;
    scenario;
    resetStoryState();
  });

  function resetStoryState(): void {
    branches = [{
      id: 'main',
      label: 'Main',
      parentId: null,
      reason: 'original',
      messages: createScenarioMessages(scenario),
    }];
    value = scenario === 'busy' ? 'This waits until the thread is ready.' : '';
    submittedCount = 0;
    interruptedCount = 0;
    lastRunConfig = 'none';
    activeThreadId = 'main';
    revisionCount = 0;
    resolvedRequestIds = [];
    requestLog = [];
    messageActionLog = [];
    pendingRuntimeRequests = triggerRuntimeRequests ? createRuntimeRequests(requestScenario) : [];
    untrack(notify);
  }

  function createStoryThread(): ThreadState {
    return {
      controller: {} as ThreadState['controller'],
      subscribe(run) {
        subscribers.add(run);
        run(createSnapshot());
        return () => {
          subscribers.delete(run);
        };
      },
      getSnapshot: createSnapshot,
      clearError: () => {},
      start: async () => {},
      rehydrate: async () => {},
      connect: async () => {},
      disconnect: async () => {},
      dispose: async () => {},
      sendMessage: async (input, options) => {
        const text = input.text ?? `${input.attachments?.length ?? 0} attachment(s)`;
        const runConfig = options?.runConfig;
        submittedCount += 1;
        lastRunConfig = runConfig ? JSON.stringify(runConfig) : 'none';
        updateActiveBranch([
          ...messages,
          createMessage(`user-${submittedCount}`, 'user', text),
          createFakeAssistantMessage(
            `assistant-${submittedCount}`,
            `Send response for "${text}"`,
            runConfig?.modelId ?? 'default',
          ),
        ]);
        notify();
      },
      run: async () => undefined,
      respond: async (input) => {
        resolveRequest('custom-1', `respond ${input.type}`);
        return undefined;
      },
      interrupt: async () => {
        interruptedCount += 1;
        notify();
      },
      approve: async (id, choice) => {
        resolveRequest(id, `approve ${id}${choice ? ` (${choice})` : ''}`);
        return undefined;
      },
      deny: async (id, reason) => {
        resolveRequest(id, `deny ${id}${reason ? `: ${reason}` : ''}`);
        return undefined;
      },
      clarify: async (id, answer) => {
        resolveRequest(id, `clarify ${id}: ${answer}`);
        return undefined;
      },
      answerClientToolRequest: async (id) => {
        resolveRequest(id, `client-tool ${id}`);
        return undefined;
      },
    };
  }

  function notify() {
    const snapshot = createSnapshot();
    for (const subscriber of subscribers) subscriber(snapshot);
  }

  function resolveRequest(id: string, log: string) {
    resolvedRequestIds = [...new Set([...resolvedRequestIds, id])];
    pendingRuntimeRequests = pendingRuntimeRequests.filter((request) => request.id !== id);
    requestLog = [log, ...requestLog].slice(0, 5);
    notify();
  }

  function logMessageAction(action: string, message: ThreadMessage): void {
    messageActionLog = [`${action} ${message.role}:${message.id}`, ...messageActionLog].slice(0, 5);
  }

  function selectBranch(id: string): void {
    activeThreadId = id;
    notify();
  }

  function updateActiveBranch(nextMessages: ThreadMessage[]): void {
    branches = branches.map((branch) =>
      branch.id === activeThreadId
        ? { ...branch, messages: nextMessages }
        : branch,
    );
  }

  async function requestRetry(message: ThreadMessage): Promise<void> {
    logMessageAction('retry-request', message);
    try {
      await revisions.forkAndRetryMessage(message.id, {
        fork: ({ inputMessageId }) => ({ name: `Retry ${inputMessageId}` }),
        runConfig: { modelId, skipTools },
      });
    } catch {
      // The revision state records and reports the error through onError.
    }
  }

  function createRevisionClient(): AgentClient {
    return {
      getThreadMessages: async () => [
        createClientMessage('system-root', 'system', 'System boundary.'),
        ...messages.map(toClientMessage),
      ],
      forkThread: async (_sessionId, _threadId, options) => {
        revisionCount += 1;
        const branchId = `fork-${revisionCount}`;
        branches = [
          ...branches,
          {
            id: branchId,
            label: options.name ?? branchId,
            parentId: activeThreadId,
            reason: options.name ?? 'revision',
            messages: [],
          },
        ];
        return createClientThread(branchId, options.name);
      },
      run: async (input: AgentRunInputEvent) => {
        const text = typeof input.text === 'string' ? input.text : 'Revision input';
        const targetThreadId = input.threadId ?? `fork-${revisionCount}`;
        branches = branches.map((branch) =>
          branch.id === targetThreadId
            ? {
                ...branch,
                messages: [
                  createMessage(`user-revision-${revisionCount}`, 'user', text),
                  createFakeAssistantMessage(
                    `assistant-revision-${revisionCount}`,
                    `${branch.label} response`,
                    typeof input.runConfig?.modelId === 'string' ? input.runConfig.modelId : 'default',
                  ),
                ],
              }
            : branch,
        );
        return undefined;
      },
    } as unknown as AgentClient;
  }

  function createClientThread(id: string, name?: string | null): ClientThread {
    return {
      id,
      sessionId: 's1',
      name: name ?? id,
      createdAt: new Date().toISOString(),
      lastActivity: new Date().toISOString(),
      messageCount: messages.length,
      kind: 'MainAgent',
      visibility: 'Visible',
      childThreads: [],
      totalForks: 0,
    };
  }

  function toClientMessage(message: ThreadMessage): ClientThreadMessage {
    return createClientMessage(message.id, message.role, message.content);
  }

  function createClientMessage(
    id: string,
    role: string,
    text: string,
  ): ClientThreadMessage {
    return {
      id,
      role,
      timestamp: new Date().toISOString(),
      contents: [{ $type: 'text', text }],
    };
  }

  function createSnapshot(): ThreadStateSnapshot {
    const blocked = scenario === 'busy' || pendingRuntimeRequests.length > 0;
    const activity = {
      status: pendingRuntimeRequests.length > 0
        ? 'requesting' as const
        : busy
          ? 'working' as const
          : 'idle' as const,
      streaming: busy,
      reasoning: false,
      activeToolCount: 0,
      pendingRequestCount: pendingRuntimeRequests.length,
    };
    const timeline = messages.map((message) => ({
      type: 'message' as const,
      id: `message:${message.id}`,
      message,
      turnId: message.turnId,
      conversationId: message.conversationId,
      executionId: message.executionId,
      eventFlowId: message.eventFlowId,
      sequenceNumber: message.sequenceNumber,
    }));
    return {
      projection: {
        thread: null,
        timeline,
        workGroups: [],
        transcriptMessages: messages,
        activeTools: [],
        pendingRuntimeRequests,
        threadExecution: busy
          ? {
              threadExecutionId: 'storybook-run',
              agentId: 'agent',
              status: 'active',
            }
          : null,
        activity,
        currentTurnId: null,
        currentConversationId: null,
        currentExecutionId: busy ? 'storybook-run' : null,
        error: null,
        canSend: !blocked,
      },
      timeline,
      workGroups: [],
      transcriptMessages: messages,
      activity,
      activeTools: [],
      pendingRuntimeRequests,
      textSubmissionState: blocked
        ? { canSubmit: false, reason: 'busy' }
        : { canSubmit: true, reason: null },
      canSubmitText: !blocked,
      loading: false,
      connected: true,
      error: null,
    };
  }

  function createRuntimeRequests(mode: RequestScenario): RuntimeRequest[] {
    const known = [permissionRequest(), clarificationRequest(), clientToolRequest()];
    const custom = [customRequest()];
    if (mode === 'permission') return [known[0]];
    if (mode === 'clarification') return [known[1]];
    if (mode === 'client-tool') return [known[2]];
    if (mode === 'custom') return custom;
    return [...known, ...custom];
  }

  function permissionRequest(): RuntimeRequest {
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
        description: 'Allow the agent to run a command.',
        callId: 'call-1',
        arguments: { command: 'npm test' },
      },
    };
  }

  function clarificationRequest(): RuntimeRequest {
    return {
      id: 'clarify-1',
      kind: 'clarification',
      sourceName: 'ClarificationFunction',
      requestEventType: 'CLARIFICATION_REQUEST',
      expectedResponseEventType: 'CLARIFICATION_RESPONSE',
      request: {
        requestId: 'clarify-1',
        sourceName: 'ClarificationFunction',
        question: 'Which environment should the agent use?',
        options: ['dev', 'prod'],
      },
    };
  }

  function clientToolRequest(): RuntimeRequest {
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
        description: 'Pick a local file for the agent.',
        arguments: { accept: 'image/*' },
      },
    };
  }

  function customRequest(): RuntimeRequest {
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

  function createScenarioMessages(mode: Scenario): ThreadMessage[] {
    if (mode === 'empty') {
      return [];
    }

    return [
      createMessage('user-initial', 'user', 'Can you inspect the current thread?'),
      {
        ...createMessage(
          'assistant-initial',
          'assistant',
          mode === 'busy'
            ? 'I am still working through the active thread execution.'
            : 'The thread projection is ready for display.',
        ),
        streaming: mode === 'busy',
        reasoning: 'The UI is reading projected messages, not protocol events.',
      },
    ];
  }

  function createMessage(
    id: string,
    role: ThreadMessage['role'],
    content: string,
  ): ThreadMessage {
    return {
      id,
      role,
      content,
      streaming: false,
      thinking: false,
      timestamp: new Date(),
      toolCalls: [],
      turnId: null,
      conversationId: null,
      executionId: null,
      placement: 'transcript',
    };
  }

  function createFakeAssistantMessage(
    id: string,
    prefix: string,
    modelId: string,
  ): ThreadMessage {
    return {
      ...createMessage(
        id,
        'assistant',
        `${prefix}: I would render this as a fresh assistant answer on the active branch using ${modelId}.`,
      ),
      reasoning: 'Fake Storybook agent response generated after the branch action.',
    };
  }

  function createEmptyBranch(): StoryBranch {
    return {
      id: 'main',
      label: 'Main',
      parentId: null,
      reason: 'empty',
      messages: [],
    };
  }

  const orderedMessages = $derived(reverse ? [...messages].reverse() : messages);
</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Composed primitive tutorial</p>
    <h1>Thread transcript + composer</h1>
    <p>
      One thread state drives the conversation: transcript messages render the
      projected leaves, runtime requests render the request lifecycle, and the
      composer sends text back through the thread.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>Read this composition</h2>
      <ol>
      <li>`ThreadConversation` is the default shell for one thread.</li>
      <li>`ThreadStatus` renders ambient thread state.</li>
        <li>`transcriptMessages` renders final transcript leaves.</li>
        <li>`ThreadRuntimeRequests` renders pending request lifecycle state.</li>
        <li>`ThreadComposer` submits text through the same thread.</li>
        <li>Message edit/retry creates a fork and surfaces the new thread id.</li>
        <li>`runConfig` is forwarded without interpretation.</li>
        <li>Custom DOM comes from snippets, not from a workspace runtime.</li>
      </ol>

      <h2>Live state</h2>
      <dl>
        <div><dt>scenario</dt><dd>{scenario}</dd></div>
        <div><dt>transcript</dt><dd>{messages.length}</dd></div>
        <div><dt>requests</dt><dd>{pendingRuntimeRequests.length}</dd></div>
        <div><dt>thread</dt><dd>{activeThreadId}</dd></div>
        <div><dt>submitted</dt><dd>{submittedCount}</dd></div>
        <div><dt>interrupts</dt><dd>{interruptedCount}</dd></div>
        <div><dt>runConfig</dt><dd>{lastRunConfig}</dd></div>
      </dl>

      <h2>Request responses</h2>
      {#if requestLog.length === 0}
        <p>No request responses yet.</p>
      {:else}
        <ul class="log">
          {#each requestLog as item}
            <li>{item}</li>
          {/each}
        </ul>
      {/if}

      <h2>Message action bar</h2>
      {#if messageActionLog.length === 0}
        <p>No message action bar yet.</p>
      {:else}
        <ul class="log">
          {#each messageActionLog as item}
            <li>{item}</li>
          {/each}
        </ul>
      {/if}

      <h2>Branches</h2>
      <div class="branches" aria-label="Story branches">
        {#each branches as branch (branch.id)}
          <button
            type="button"
            class:active-branch={branch.id === activeThreadId}
            onclick={() => selectBranch(branch.id)}
          >
            <strong>{branch.label}</strong>
            <span>{branch.id}</span>
            {#if branch.parentId}
              <small>from {branch.parentId}</small>
            {:else}
              <small>{branch.reason}</small>
            {/if}
          </button>
        {/each}
      </div>
    </aside>

    <main class="conversation" data-busy={busy}>
      {#if renderMode === 'shell'}
        <ThreadConversation
          {thread}
          class="conversation-shell"
          viewportProps={{
            class: 'conversation-viewport',
            turnAnchor: 'top',
          }}
        >
          {#snippet header()}
            <ThreadStatus {thread} class="status" />
          {/snippet}

          {#snippet timeline({ snapshot })}
            <ThreadTimeline {thread} timeline={snapshot.timeline} class="conversation-timeline">
              {#snippet empty()}
                <div class="empty">No messages yet. Send the first one.</div>
              {/snippet}

              {#snippet message({ message })}
                <Message
                  {message}
                  showActions
                  class="message"
                  onCopy={({ message }) => logMessageAction('copy', message)}
                  onEditRequest={canEditMessage(message)
                    ? () => logMessageAction('edit-request', message)
                    : undefined}
                  onRetryRequest={canRetryMessage(message)
                    ? ({ message }) => void requestRetry(message)
                    : undefined}
                >
                  {#snippet children({ message, status })}
                    <header>
                      <strong>{message.role}</strong>
                      <span>{status}</span>
                    </header>
                    {#if message.reasoning}
                      <small>{message.reasoning}</small>
                    {/if}
                    <p>{message.content}</p>
                  {/snippet}
                </Message>
              {/snippet}
            </ThreadTimeline>
          {/snippet}

          {#snippet requests()}
            <ThreadRuntimeRequests {thread}>
              {#snippet empty()}
                {#if triggerRuntimeRequests}
                  <div class="empty request-empty">All triggered requests have been answered.</div>
                {/if}
              {/snippet}
            </ThreadRuntimeRequests>
          {/snippet}

          {#snippet footer()}
            <ThreadScrollToBottom class="scroll-button">Jump to latest</ThreadScrollToBottom>

            <ThreadComposer
              {thread}
              bind:value
              autosize={autosizeStrategy}
              {clear}
              {submitMode}
              minRows={1}
              maxRows={7}
              pretext={{ font: '16px Inter', lineHeight: 22 }}
              runConfig={{ modelId, skipTools }}
            >
              {#snippet child({ state, props })}
                <form {...props.root} class="composer shell-composer">
                  {#if showRunConfig}
                    <div class="toolbar">
                      <label>
                        Model
                        <select bind:value={modelId}>
                          <option value="fast">fast</option>
                          <option value="careful">careful</option>
                        </select>
                      </label>

                      <label class="check">
                        <input type="checkbox" bind:checked={skipTools} />
                        Skip tools
                      </label>
                    </div>
                  {/if}

                  <textarea {...props.input} {@attach props.inputAttachment}></textarea>

                  <div class="actions">
                    <span>{state.blockedReason ?? 'ready'}</span>
                    <button {...props.interrupt}>Interrupt</button>
                    <button {...props.submit}>Send</button>
                  </div>
                </form>
              {/snippet}
            </ThreadComposer>
          {/snippet}
        </ThreadConversation>
      {:else}
        <ThreadStatus {thread} class="status" />

        <div data-hpd-thread-transcript>
          {#if orderedMessages.length === 0}
            <div class="empty">No messages yet. Send the first one.</div>
          {:else}
            {#each orderedMessages as message (message.id)}
              <MessageEdit
                {message}
                {revisions}
                class="message-edit"
                autosize={autosizeStrategy}
                minRows={2}
                maxRows={6}
                pretext={{ font: '15px Inter', lineHeight: 21 }}
                runConfig={{ modelId, skipTools }}
                forkOptions={({ inputMessageId, sentText }) => ({
                  name: `Edit ${inputMessageId}`,
                  metadata: {
                    replacementPreview: sentText.slice(0, 120),
                  },
                })}
                onStartEdit={({ message }) => logMessageAction('edit-request', message)}
                onError={({ error }) => {
                  messageActionLog = [`edit failed: ${error.message}`, ...messageActionLog].slice(0, 5);
                }}
              >
                {#snippet view({ actions, message })}
                  <Message
                    {message}
                    showActions
                    class="message"
                    onCopy={({ message }) => logMessageAction('copy', message)}
                    onEditRequest={canEditMessage(message)
                      ? () => actions.startEdit()
                      : undefined}
                    onRetryRequest={canRetryMessage(message)
                      ? ({ message }) => void requestRetry(message)
                      : undefined}
                  >
                    {#snippet children({ message, status })}
                      <header>
                        <strong>{message.role}</strong>
                        <span>{status}</span>
                      </header>
                      <p>{message.content}</p>
                      {#if message.reasoning}
                        <small>{message.reasoning}</small>
                      {/if}
                    {/snippet}
                  </Message>
                {/snippet}

                {#snippet edit({ actionProps, actions, props, textareaAttachment, pending, canSave })}
                  <div class="edit-draft">
                    <label>
                      Replacement user message
                      <textarea {...props.textarea} {@attach textareaAttachment}></textarea>
                    </label>
                    <div>
                      <button {...actionProps.cancel} onclick={actions.cancel}>Cancel</button>
                      <button {...actionProps.save} onclick={actions.save}>
                        {pending ? 'Forking...' : 'Fork with replacement'}
                      </button>
                    </div>
                    {#if !canSave}
                      <small>Enter replacement text to create a fork.</small>
                    {/if}
                  </div>
                {/snippet}
              </MessageEdit>
            {/each}
          {/if}
        </div>

        <ThreadRuntimeRequests {thread}>
          {#snippet empty()}
            {#if triggerRuntimeRequests}
              <div class="empty request-empty">All triggered requests have been answered.</div>
            {/if}
          {/snippet}
        </ThreadRuntimeRequests>

        <ThreadComposer
          {thread}
          bind:value
          autosize={autosizeStrategy}
          {clear}
          {submitMode}
          minRows={1}
          maxRows={7}
          pretext={{ font: '16px Inter', lineHeight: 22 }}
          runConfig={{ modelId, skipTools }}
        >
          {#snippet child({ state, props })}
            <form {...props.root} class="composer">
              {#if showRunConfig}
                <div class="toolbar">
                  <label>
                    Model
                    <select bind:value={modelId}>
                      <option value="fast">fast</option>
                      <option value="careful">careful</option>
                    </select>
                  </label>

                  <label class="check">
                    <input type="checkbox" bind:checked={skipTools} />
                    Skip tools
                  </label>
                </div>
              {/if}

              <textarea {...props.input} {@attach props.inputAttachment}></textarea>

              <div class="actions">
                <span>{state.blockedReason ?? 'ready'}</span>
                <button {...props.interrupt}>Interrupt</button>
                <button {...props.submit}>Send</button>
              </div>
            </form>
          {/snippet}
        </ThreadComposer>
      {/if}
    </main>
  </div>
</section>

<style>
  .tutorial {
    min-height: 100%;
    padding: 28px;
    background: #f6f4ef;
    color: #20231f;
    font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  .intro {
    max-width: 980px;
    margin: 0 auto 24px;
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
    max-width: 980px;
    margin: 0 auto;
  }

  .guide,
  .conversation {
    border: 1px solid #d8d2c4;
    border-radius: 8px;
    background: #fffdf8;
  }

  .guide {
    padding: 18px;
  }

  .guide ol {
    padding-left: 18px;
  }

  .log {
    padding-left: 18px;
  }

  .branches {
    display: grid;
    gap: 8px;
  }

  .branches button {
    display: grid;
    height: auto;
    justify-items: start;
    gap: 2px;
    border-color: #c5cec3;
    background: #ffffff;
    color: #27312c;
    text-align: left;
  }

  .branches button span,
  .branches button small {
    color: #6a746d;
    font-size: 12px;
  }

  .branches button.active-branch {
    border-color: #2f665d;
    background: #e8f1ee;
  }

  .guide li,
  .guide dd,
  .guide dt {
    font-size: 13px;
  }

  .guide dl {
    display: grid;
    gap: 10px;
  }

  .guide dl div {
    display: grid;
    gap: 2px;
  }

  .guide dt {
    color: #6f756d;
    font-weight: 700;
  }

  .guide dd {
    margin: 0;
    overflow-wrap: anywhere;
  }

  .conversation {
    display: grid;
    grid-template-rows: auto minmax(260px, 1fr) auto auto;
    min-height: 560px;
    overflow: hidden;
  }

  :global(.conversation-shell) {
    min-height: 560px;
    display: grid;
    grid-template-rows: auto minmax(0, 1fr);
    overflow: hidden;
  }

  :global(.conversation-viewport) {
    min-height: 0;
    display: grid;
    grid-template-rows: minmax(0, 1fr) auto;
    overflow: auto;
    scroll-padding-bottom: 140px;
    background: linear-gradient(180deg, #fffdf8 0%, #faf7f0 100%);
  }

  .status {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    padding: 10px 18px;
    border-bottom: 1px solid #d8d2c4;
    color: #516058;
    font-size: 13px;
  }

  .status[data-status-state='requesting'] {
    background: #fffbea;
  }

  .status[data-status-state='working'] {
    background: #f1f8fb;
  }

  .status[data-status-state='error'] {
    background: #fff7f6;
  }

  :global([data-hpd-thread-messages]),
  :global(.conversation-timeline) {
    align-content: start;
    display: grid;
    gap: 18px;
    overflow: auto;
    padding: 24px;
  }

  [data-hpd-thread-transcript] {
    align-content: start;
    display: grid;
    gap: 18px;
    overflow: auto;
    padding: 24px;
  }

  :global(.message) {
    width: min(76%, 620px);
    padding: 14px 16px;
    border: 1px solid #d9ded4;
    border-radius: 8px;
    background: #f8faf6;
  }

  :global(.message[data-role='user']) {
    justify-self: end;
    background: #e8f1ee;
    border-color: #c7dad3;
  }

  :global(.message header) {
    display: flex;
    justify-content: space-between;
    gap: 12px;
    margin-bottom: 8px;
    color: #566159;
    font-size: 12px;
    text-transform: uppercase;
  }

  :global(.message p) {
    margin-bottom: 0;
  }

  :global(.message small) {
    display: block;
    margin-top: 8px;
    color: #656d63;
  }

  :global(.message [data-hpd-message-action-bar]) {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    flex-wrap: wrap;
    margin-top: 12px;
    padding-top: 10px;
    border-top: 1px solid #dde4d9;
  }

  :global(.message [data-hpd-message-action]) {
    height: 28px;
    border-color: #c4cec0;
    background: white;
    color: #344039;
    font-size: 12px;
  }

  .edit-draft {
    justify-self: end;
    width: min(76%, 620px);
    display: grid;
    gap: 10px;
    padding: 12px;
    border: 1px solid #b9d2ca;
    border-radius: 8px;
    background: #f0f7f4;
  }

  .edit-draft label {
    display: grid;
    gap: 8px;
    color: #40534c;
    font-size: 13px;
    font-weight: 700;
  }

  .edit-draft div {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    flex-wrap: wrap;
  }

  .edit-draft button[type='button'] {
    border-color: #b8c5be;
    background: white;
    color: #33413b;
  }

  .empty {
    padding: 18px;
    border: 1px dashed #bfc7bd;
    border-radius: 8px;
    color: #5d675f;
  }

  .request-empty {
    margin: 0 18px 14px;
  }

  :global([data-hpd-thread-runtime-requests]) {
    display: grid;
    gap: 10px;
    padding: 0 18px 14px;
  }

  :global([data-hpd-runtime-request]) {
    display: grid;
    gap: 10px;
    padding: 12px;
    border: 1px solid #cbd8cf;
    border-radius: 8px;
    background: #f6faf7;
  }

  :global([data-hpd-runtime-request-header]),
  :global([data-hpd-runtime-request-actions]) {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    flex-wrap: wrap;
  }

  :global([data-hpd-runtime-request-header]) {
    color: #526159;
    font-size: 12px;
    text-transform: uppercase;
  }

  :global([data-hpd-runtime-request-field]) {
    display: grid;
    gap: 6px;
    color: #5c655d;
    font-size: 13px;
  }

  :global([data-hpd-runtime-request-arguments]),
  :global([data-hpd-runtime-request-event]) {
    overflow: auto;
    margin: 0;
    padding: 10px;
    border-radius: 6px;
    background: #eef2ec;
  }

  :global([data-hpd-thread-timeline-viewport-footer]) {
    position: sticky;
    bottom: 0;
    display: grid;
    gap: 10px;
    padding: 8px 14px 14px;
    border-top: 1px solid #d8d2c4;
    background: color-mix(in srgb, #fbf8f1 92%, transparent);
    backdrop-filter: blur(10px);
  }

  :global(.scroll-button) {
    justify-self: center;
    height: 30px;
    border-color: #c8d1c6;
    background: #ffffff;
    color: #405047;
    font-size: 12px;
    box-shadow: 0 8px 22px rgba(45, 54, 48, 0.12);
  }

  :global(.scroll-button:disabled) {
    opacity: 0;
    pointer-events: none;
  }

  .composer {
    display: grid;
    gap: 10px;
    padding: 14px;
    border-top: 1px solid #d8d2c4;
    background: #fbf8f1;
  }

  .shell-composer {
    border: 1px solid #d8d2c4;
    border-radius: 8px;
    background: #fffdf8;
    box-shadow: 0 10px 28px rgba(49, 45, 35, 0.08);
  }

  .toolbar,
  .actions {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
    flex-wrap: wrap;
  }

  .toolbar label,
  .actions span {
    color: #5c655d;
    font-size: 13px;
  }

  .toolbar label {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }

  .check {
    user-select: none;
  }

  select,
  textarea,
  button {
    font: inherit;
  }

  select {
    height: 32px;
    border: 1px solid #c5cec3;
    border-radius: 6px;
    background: white;
  }

  textarea {
    min-height: 44px;
    width: 100%;
    box-sizing: border-box;
    resize: none;
    border: 1px solid #c5cec3;
    border-radius: 8px;
    padding: 10px 12px;
    background: white;
  }

  button {
    height: 34px;
    border: 1px solid #2f665d;
    border-radius: 6px;
    padding: 0 12px;
    background: #2f665d;
    color: white;
    cursor: pointer;
  }

  button:disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

  button[data-hpd-thread-composer-interrupt] {
    border-color: #b8c2b6;
    background: white;
    color: #29332c;
  }

  @media (max-width: 760px) {
    .tutorial {
      padding: 18px;
    }

    .layout {
      grid-template-columns: 1fr;
    }

    .conversation {
      min-height: 520px;
    }

    :global(.message) {
      width: 100%;
      box-sizing: border-box;
    }
  }
</style>
