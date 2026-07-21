<script lang="ts">
  import {
    Message,
    MessageEdit,
    createThreadRevisionState,
  } from '../src/index.js';
  import type { Message as ThreadMessage } from '@hpd-research/hpd-agent-headless-ui';
  import type {
    AgentClient,
    Thread,
    ThreadMessage as ClientThreadMessage,
  } from '@hpd-research/hpd-agent-client';

  let {
    initialContent = 'Explain the thread projection in one paragraph.',
    failSave = false,
    submitMode = 'enter',
  }: {
    initialContent?: string;
    failSave?: boolean;
    submitMode?: 'enter' | 'mod-enter' | 'none';
  } = $props();

  let revisionCount = $state(0);
  let log = $state<string[]>([]);

  const message = $derived<ThreadMessage>({
    id: 'user-1',
    role: 'user',
    content: initialContent,
    streaming: false,
    thinking: false,
    timestamp: new Date('2026-01-01T00:00:00.000Z'),
    toolCalls: [],
    turnId: null,
    conversationId: null,
    executionId: null,
    placement: 'transcript',
  });

  const revisions = createThreadRevisionState({
    client: createClient(),
    agentId: 'agent',
    sessionId: 's1',
    threadId: 'main',
    onRevisionCreated: (revision) => {
      log = [
        `created ${revision.threadId}: ${revision.sentText}`,
        ...log,
      ].slice(0, 5);
    },
    onError: (error) => {
      log = [`failed: ${error.message}`, ...log].slice(0, 5);
    },
  });

  function createClient(): AgentClient {
    return {
      getThreadMessages: async () => [
        createClientMessage('user-1', 'user', message.content),
        createClientMessage('assistant-1', 'assistant', 'Initial response.'),
      ],
      forkThread: async (_sessionId, _threadId, options) => {
        if (failSave) throw new Error('Story-configured fork failure.');
        revisionCount += 1;
        return createThread(`fork-${revisionCount}`, options.name);
      },
      run: async () => undefined,
    } as unknown as AgentClient;
  }

  function createClientMessage(id: string, role: string, text: string): ClientThreadMessage {
    return {
      id,
      role,
      timestamp: '2026-01-01T00:00:00.000Z',
      contents: [{ $type: 'text', text }],
    };
  }

  function createThread(id: string, name?: string | null): Thread {
    return {
      id,
      sessionId: 's1',
      name: name ?? id,
      createdAt: '2026-01-01T00:00:00.000Z',
      lastActivity: '2026-01-01T00:00:00.000Z',
      messageCount: 1,
      kind: 'MainAgent',
      visibility: 'Visible',
      childThreads: [],
      totalForks: 0,
    };
  }
</script>

<section class="demo">
  <div class="surface">
    <MessageEdit
      {message}
      {revisions}
      class="edit"
      forkOptions={({ inputMessageId, sentText }) => ({
        name: `Edit ${inputMessageId}`,
        metadata: {
          replacementPreview: sentText.slice(0, 120),
        },
      })}
      runConfig={{ modelId: 'story-model' }}
      {submitMode}
    >
      {#snippet view({ actions, message })}
        <Message
          {message}
          showActions
          class="message"
          onEditRequest={actions.startEdit}
          onCopy={({ text }) => {
            log = [`copied: ${text}`, ...log].slice(0, 5);
          }}
        />
      {/snippet}

      {#snippet edit({ actionProps, actions, props, textareaAttachment, pending, canSave, error })}
        <div class="editor">
          <textarea {...props.textarea} {@attach textareaAttachment}></textarea>
          <div class="bar">
            <button {...actionProps.cancel} onclick={actions.cancel}>Cancel</button>
            <button {...actionProps.save} onclick={actions.save}>
              {pending ? 'Forking...' : 'Fork with edit'}
            </button>
          </div>
          {#if !canSave}
            <small>Replacement text is required.</small>
          {/if}
          {#if error}
            <small class="error">{error.message}</small>
          {/if}
        </div>
      {/snippet}
    </MessageEdit>
  </div>

  <aside>
    <h2>Events</h2>
    {#if log.length === 0}
      <p>No edits yet.</p>
    {:else}
      <ul>
        {#each log as item}
          <li>{item}</li>
        {/each}
      </ul>
    {/if}
  </aside>
</section>

<style>
  .demo {
    min-height: 100%;
    display: grid;
    grid-template-columns: minmax(0, 1fr) 280px;
    gap: 20px;
    padding: 28px;
    background: #f7f5f0;
    color: #20231f;
    font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  .surface,
  aside {
    border: 1px solid #d8d2c4;
    border-radius: 8px;
    background: #fffdf8;
    padding: 18px;
  }

  .message {
    display: grid;
    gap: 10px;
  }

  .editor {
    display: grid;
    gap: 10px;
  }

  textarea {
    width: 100%;
    box-sizing: border-box;
    resize: none;
    border: 1px solid #c8c0b2;
    border-radius: 8px;
    padding: 10px 12px;
    font: inherit;
    line-height: 1.45;
  }

  .bar {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
  }

  button {
    border: 1px solid #b9b1a4;
    border-radius: 6px;
    background: #ffffff;
    padding: 7px 10px;
    font: inherit;
  }

  button[data-hpd-message-edit-save] {
    background: #1f6f62;
    border-color: #1f6f62;
    color: #ffffff;
  }

  button:disabled {
    opacity: 0.55;
  }

  aside h2 {
    margin: 0 0 12px;
    font-size: 14px;
  }

  ul {
    margin: 0;
    padding-left: 18px;
  }

  small {
    color: #6f6659;
  }

  .error {
    color: #a63b31;
  }

  @media (max-width: 760px) {
    .demo {
      grid-template-columns: 1fr;
      padding: 16px;
    }
  }
</style>
