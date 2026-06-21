<script lang="ts">
  import {
    createComposerDirectiveAdditionalProperties,
    createStaticComposerTriggerAdapter,
  } from '@hpd-research/hpd-agent-headless-ui';
  import {
    ComposerTriggerAction,
    ComposerTriggerDirective,
    ComposerTriggerItem,
    ComposerTriggerItems,
    ComposerTriggerPopover,
    ComposerTriggerRoot,
    ThreadComposer,
    type ThreadState,
    type ThreadStateSnapshot,
    type ThreadComposerRunConfig,
  } from '../src/index.js';
  import type { ThreadProjectionSnapshot } from '@hpd-research/hpd-agent-headless-ui';

  let {
    initialValue = 'Ask @wor about /deep',
    renderMode = 'default',
  }: {
    initialValue?: string;
    renderMode?: 'default' | 'custom';
  } = $props();

  let value = $state(initialValue);
  let cursor = $state(initialValue.length);
  let textareaRef = $state<HTMLTextAreaElement | null>(null);
  let additionalProperties = $state<Record<string, unknown> | undefined>();
  let runConfig = $state<ThreadComposerRunConfig | undefined>();
  let selected = $state('none');

  const thread = createStoryThread();
  const mentionAdapter = createStaticComposerTriggerAdapter({
    items: [
      {
        id: 'workspace',
        type: 'tool',
        label: 'Workspace',
        description: 'Current workspace context',
      },
      {
        id: 'docs',
        type: 'file',
        label: 'Docs',
        description: 'Internal documentation',
      },
    ],
  });
  const commandAdapter = createStaticComposerTriggerAdapter({
    items: [
      {
        id: 'deep',
        type: 'command',
        label: '/deep',
        description: 'Use the deeper reasoning run profile',
        metadata: {
          modelId: 'deep-model',
        },
      },
      {
        id: 'fast',
        type: 'command',
        label: '/fast',
        description: 'Use the fast run profile',
        metadata: {
          modelId: 'fast-model',
        },
      },
    ],
  });

  function createStoryThread(): ThreadState {
    const activity = {
      status: 'idle' as const,
      streaming: false,
      reasoning: false,
      activeToolCount: 0,
      pendingRequestCount: 0,
    };
    const projection: ThreadProjectionSnapshot = {
      thread: null,
      timeline: [],
      workGroups: [],
      transcriptMessages: [],
      activeTools: [],
      pendingRuntimeRequests: [],
      threadRun: null,
      activity,
      currentTurnId: null,
      currentConversationId: null,
      currentRunId: null,
      error: null,
      canSend: true,
    };
    const snapshot: ThreadStateSnapshot = {
      projection,
      timeline: [],
      workGroups: [],
      transcriptMessages: [],
      activity,
      activeTools: [],
      pendingRuntimeRequests: [],
      textSubmissionState: { canSubmit: true, reason: null },
      canSubmitText: true,
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
</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Composer primitive tutorial</p>
    <h1>Composer triggers</h1>
    <p>
      Type after `@` or `/`, place the cursor at the end, then select an item.
      Mentions patch message metadata; commands patch run config.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>Live state</h2>
      <dl>
        <div>
          <dt>value</dt>
          <dd>{value || 'empty'}</dd>
        </div>
        <div>
          <dt>cursor</dt>
          <dd>{cursor}</dd>
        </div>
        <div>
          <dt>selected</dt>
          <dd>{selected}</dd>
        </div>
        <div>
          <dt>metadata</dt>
          <dd>{JSON.stringify(additionalProperties ?? null)}</dd>
        </div>
        <div>
          <dt>run config</dt>
          <dd>{JSON.stringify(runConfig ?? null)}</dd>
        </div>
      </dl>
    </aside>

    <main class="preview">
      <ComposerTriggerRoot
        bind:value
        bind:cursor
        bind:inputRef={textareaRef}
        bind:additionalProperties
        bind:runConfig
        class="trigger-root"
      >
        <ThreadComposer
          {thread}
          bind:value
          bind:textareaRef={textareaRef}
          {additionalProperties}
          {runConfig}
          class="composer"
        />

        <ComposerTriggerPopover trigger="@" adapter={mentionAdapter} class="popover mention">
          <ComposerTriggerDirective
            additionalProperties={({ item, result }) => createComposerDirectiveAdditionalProperties({
              item,
              trigger: result.trigger,
            })}
            onInserted={({ item }) => {
              selected = `mention:${item.id}`;
            }}
          />

          <ComposerTriggerItems>
            {#snippet children({ items })}
              {#each items as item, index (item.id)}
                {#if renderMode === 'custom'}
                  <ComposerTriggerItem {item} {index} class="item">
                    {#snippet children({ highlighted, item, props, select })}
                      <button {...props} class:item-active={highlighted} onclick={() => select()}>
                        <strong>{item.label}</strong>
                        <span>{item.description}</span>
                      </button>
                    {/snippet}
                  </ComposerTriggerItem>
                {:else}
                  <ComposerTriggerItem {item} {index} class="item" />
                {/if}
              {/each}
            {/snippet}
          </ComposerTriggerItems>
        </ComposerTriggerPopover>

        <ComposerTriggerPopover trigger="/" adapter={commandAdapter} class="popover command">
          <ComposerTriggerAction
            removeOnExecute
            onExecute={({ item }) => {
              selected = `command:${item.id}`;
              return {
                runConfigPatch: {
                  modelId: String(item.metadata?.modelId ?? item.id),
                  contextOverrides: {
                    command: item.id,
                  },
                },
              };
            }}
          />

          <ComposerTriggerItems>
            {#snippet children({ items })}
              {#each items as item, index (item.id)}
                <ComposerTriggerItem {item} {index} class="item" />
              {/each}
            {/snippet}
          </ComposerTriggerItems>
        </ComposerTriggerPopover>
      </ComposerTriggerRoot>
    </main>
  </div>
</section>

<style>
  .tutorial {
    color: #181c1f;
    display: grid;
    gap: 1.5rem;
    padding: 2rem;
  }

  .intro {
    max-width: 72rem;
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

  .intro p:last-child {
    font-size: 1.1rem;
    line-height: 1.45;
    margin: 0;
    max-width: 56rem;
  }

  .layout {
    display: grid;
    gap: 1.5rem;
    grid-template-columns: minmax(16rem, 24rem) minmax(0, 1fr);
  }

  .guide {
    border: 1px solid #d8d3c7;
    border-radius: 8px;
    padding: 1.25rem;
  }

  .guide h2 {
    font-size: 1.35rem;
    margin: 0 0 1rem;
  }

  dl {
    display: grid;
    gap: 0.9rem;
    margin: 0;
  }

  dt {
    color: #66706e;
    font-size: 0.78rem;
    font-weight: 700;
    text-transform: uppercase;
  }

  dd {
    font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
    margin: 0;
    overflow-wrap: anywhere;
  }

  .preview {
    border: 1px solid #d8d3c7;
    border-radius: 8px;
    min-height: 24rem;
    padding: 1rem;
  }

  .trigger-root {
    display: grid;
    gap: 0.75rem;
  }

  .composer :global(textarea) {
    min-height: 8rem;
  }

  .popover {
    border: 1px solid #c4b9a6;
    border-radius: 8px;
    display: grid;
    gap: 0.35rem;
    max-width: 30rem;
    padding: 0.5rem;
  }

  .popover[hidden] {
    display: none;
  }

  .popover::before {
    color: #66706e;
    font-size: 0.78rem;
    font-weight: 700;
    text-transform: uppercase;
  }

  .mention::before {
    content: '@ mentions';
  }

  .command::before {
    content: '/ commands';
  }

  .item,
  .item :global(button) {
    align-items: start;
    background: #fffdf8;
    border: 1px solid #d8d3c7;
    border-radius: 6px;
    color: #181c1f;
    cursor: pointer;
    display: grid;
    gap: 0.2rem;
    padding: 0.65rem 0.75rem;
    text-align: left;
    width: 100%;
  }

  .item:hover,
  .item-active {
    border-color: #2b7a68;
  }

  .item span,
  .item :global(span) {
    color: #66706e;
    font-size: 0.88rem;
  }
</style>
