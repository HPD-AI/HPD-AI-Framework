<script lang="ts">
  import {
    Suggestion,
    SuggestionList,
    ThreadComposer,
    type ThreadState,
    type ThreadStateSnapshot,
  } from '../src/index.js';
  import type { ThreadProjectionSnapshot } from '@hpd-research/hpd-agent-headless-ui';

  type RenderMode = 'default' | 'children' | 'child';
  type SuggestionScenario = 'populate' | 'send-ready' | 'send-busy';

  let {
    renderMode = 'default',
    scenario = 'populate',
  }: {
    renderMode?: RenderMode;
    scenario?: SuggestionScenario;
  } = $props();

  let draft = $state('');
  let selected = $state('');
  let sentCount = $state(0);

  const thread = $derived(createStoryThread(createSnapshot(scenario)));
  const mode = $derived(scenario === 'populate' ? 'populate' : 'send');
  const suggestions = [
    {
      description: 'Plain language overview',
      prompt: 'Explain the architecture in plain language.',
      title: 'Explain',
    },
    {
      description: 'Look for defects and coverage gaps',
      prompt: 'Review this code for likely bugs and missing tests.',
      title: 'Find bugs',
    },
    {
      description: 'Recap decisions and next steps',
      prompt: 'Summarize the current thread and list next steps.',
      title: 'Summarize',
    },
  ];

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
      sendMessage: async () => {
        sentCount += 1;
      },
      run: async () => undefined,
      respond: async () => undefined,
      interrupt: async () => {},
      approve: async () => undefined,
      deny: async () => undefined,
      clarify: async () => undefined,
      answerClientToolRequest: async () => undefined,
    };
  }

  function createSnapshot(currentScenario: SuggestionScenario): ThreadStateSnapshot {
    const canSubmitText = currentScenario !== 'send-busy';
    const activity = {
      status: canSubmitText ? 'idle' as const : 'working' as const,
      streaming: !canSubmitText,
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
      threadExecution: canSubmitText
        ? null
        : {
            threadExecutionId: 'storybook-run',
            agentId: 'agent',
            status: 'active',
          },
      activity,
      currentTurnId: null,
      currentConversationId: null,
      currentExecutionId: canSubmitText ? null : 'storybook-run',
      error: null,
      canSend: canSubmitText,
    };

    return {
      projection,
      timeline: [],
      workGroups: [],
      transcriptMessages: [],
      activity,
      activeTools: [],
      pendingRuntimeRequests: [],
      textSubmissionState: canSubmitText
        ? { canSubmit: true, reason: null }
        : { canSubmit: false, reason: 'busy' },
      canSubmitText,
      loading: false,
      connected: true,
      error: null,
    };
  }
</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Headless Svelte adapter</p>
    <h1>Suggestion tutorial playground</h1>
    <p>
      `Suggestion` renders suggested prompts that populate composer draft state
      or send through a thread state.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>What to inspect</h2>
      <ol>
        <li>Populate mode updates the bound composer value.</li>
        <li>Send mode calls `ThreadState.sendMessage`.</li>
        <li>Busy send mode exposes `data-blocked-reason`.</li>
      </ol>
      <dl>
        <div>
          <dt>Draft</dt>
          <dd>{draft || 'empty'}</dd>
        </div>
        <div>
          <dt>Selected</dt>
          <dd>{selected || 'none'}</dd>
        </div>
        <div>
          <dt>Sent</dt>
          <dd>{sentCount}</dd>
        </div>
      </dl>
    </aside>

    <main class="preview">
      <div class="suggestions">
        {#if renderMode === 'child'}
          <SuggestionList
            {thread}
            {mode}
            {suggestions}
            bind:targetValue={draft}
            onSelect={(details) => {
              selected = details.prompt;
            }}
          >
            {#snippet suggestion({ actions, blockedReason, props, suggestion })}
              <button {...props} class="suggestion custom" onclick={() => actions.select()}>
                <strong>{suggestion.title}</strong>
                <span>{blockedReason ?? 'ready'}</span>
              </button>
            {/snippet}
          </SuggestionList>
        {:else if renderMode === 'children'}
          {#each suggestions as item}
            <Suggestion
              {thread}
              mode={mode}
              description={item.description}
              prompt={item.prompt}
              title={item.title}
              bind:targetValue={draft}
              class="suggestion"
              onSelect={(details) => {
                selected = details.prompt;
              }}
            >
              {#snippet children({ mode, title, description })}
                <span>{title}</span>
                <small>{description || mode}</small>
              {/snippet}
            </Suggestion>
          {/each}
        {:else}
          {#each suggestions as item}
            <Suggestion
              {thread}
              mode={mode}
              description={item.description}
              prompt={item.prompt}
              title={item.title}
              bind:targetValue={draft}
              class="suggestion"
              onSelect={(details) => {
                selected = details.prompt;
              }}
            />
          {/each}
        {/if}
      </div>

      <ThreadComposer {thread} bind:value={draft} class="composer" />
    </main>
  </div>
</section>

<style>
  .tutorial {
    min-height: 100%;
    padding: 28px;
    background: #f4f7f2;
    color: #20231f;
    font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  .intro,
  .layout {
    max-width: 960px;
    margin: 0 auto;
  }

  .intro {
    margin-bottom: 24px;
  }

  .eyebrow {
    margin: 0 0 6px;
    color: #446f39;
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
    border: 1px solid #d1dccb;
    border-radius: 8px;
    background: #fffefb;
  }

  .guide {
    padding: 18px;
  }

  .guide ol {
    padding-left: 18px;
  }

  .guide li,
  .guide dd,
  .guide dt {
    font-size: 13px;
  }

  .guide dl {
    display: grid;
    gap: 10px;
    margin: 18px 0 0;
  }

  .guide div {
    display: grid;
    gap: 2px;
  }

  .guide dt {
    color: #66705f;
    font-weight: 700;
  }

  .guide dd {
    margin: 0;
  }

  .preview {
    display: grid;
    align-content: start;
    gap: 18px;
    min-height: 300px;
    padding: 18px;
  }

  .suggestions {
    display: flex;
    flex-wrap: wrap;
    gap: 10px;
  }

  .suggestion {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    min-height: 36px;
    padding: 0 12px;
    border: 1px solid #adc5a3;
    border-radius: 999px;
    background: #f6fbf3;
    color: #253024;
    font: inherit;
    cursor: pointer;
  }

  .suggestion[data-blocked-reason] {
    opacity: 0.55;
    cursor: not-allowed;
  }

  .suggestion span {
    color: #5f6b5a;
    font-size: 12px;
  }

  .suggestion.custom {
    border-radius: 8px;
  }

  .composer {
    display: grid;
    gap: 10px;
  }

  :global(.composer textarea) {
    min-height: 92px;
    padding: 10px;
    border: 1px solid #c8d5c1;
    border-radius: 8px;
    font: inherit;
  }

  :global(.composer button) {
    width: fit-content;
    padding: 8px 12px;
    border: 1px solid #9ab090;
    border-radius: 8px;
    background: #edf6e8;
    font: inherit;
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
