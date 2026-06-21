<script lang="ts">
  import type { AgentClient, Session } from '@hpd-research/hpd-agent-client';
  import {
    createSessionListState,
    SessionListDelete,
    SessionListItem,
    SessionListItems,
    SessionListNew,
    SessionListRoot,
    SessionListSubtitle,
    SessionListTitle,
  } from '../src/index.js';

  type RenderMode = 'default' | 'item' | 'empty' | 'error';

  let {
    renderMode = 'default',
  }: {
    renderMode?: RenderMode;
  } = $props();

  const stamp = '2026-01-01T00:00:00.000Z';
  const storySessions: Session[] = [
    createSession('s-workspace-plan', {
      'hpdos.name': 'Planning',
      'hpdos.workspaceKey': 'alpha',
      description: 'Architecture notes',
    }),
    createSession('s-debugging', {
      name: 'Debugging',
      'hpdos.workspaceKey': 'alpha',
    }),
    createSession('s-side-project', {
      name: 'Side project',
      'hpdos.workspaceKey': 'beta',
    }),
  ];

  const sessionList = createSessionListState({
    client: createStoryClient(() => renderMode === 'empty' ? [] : storySessions),
    search: { metadata: { 'hpdos.workspaceKey': 'alpha' } },
  });

  let selected = $state('none');

  $effect(() => {
    if (renderMode !== 'error') {
      sessionList.load();
    }
  });

  function createSession(id: string, metadata: Record<string, unknown>): Session {
    return {
      id,
      createdAt: stamp,
      lastActivity: stamp,
      metadata,
    };
  }

  function createStoryClient(getSessions: () => Session[]): AgentClient {
    return {
      searchSessions: async (request = {}) => {
        if (renderMode === 'error') throw new Error('Could not load sessions');
        const filter = request.metadata ?? {};
        return getSessions().filter((session) =>
          Object.entries(filter).every(([key, value]) => session.metadata[key] === value));
      },
      createSession: async (request = {}) => createSession(request.sessionId ?? 'new-session', request.metadata ?? {}),
      updateSession: async (sessionId, request) => {
        const sessions = getSessions();
        const session = sessions.find((item) => item.id === sessionId) ?? createSession(sessionId, {});
        return {
          ...session,
          metadata: {
            ...session.metadata,
            ...request.metadata,
          },
        };
      },
      deleteSession: async () => {},
    } as unknown as AgentClient;
  }
</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Headless Svelte adapter</p>
    <h1>Session list primitive playground</h1>
    <p>
      Session list primitives render durable HPD sessions while leaving app
      concepts like workspaces to metadata filters and snippets.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>What to inspect</h2>
      <ol>
        <li>The controller searches by metadata.</li>
        <li>Rows expose labels, ids, selection, and raw metadata.</li>
        <li>Custom snippets can render workspace/project information.</li>
      </ol>
      <dl>
        <div>
          <dt>Selected</dt>
          <dd>{selected}</dd>
        </div>
      </dl>
    </aside>

    <main class="preview">
      <SessionListRoot {sessionList}>
        <div class="toolbar">
          <SessionListNew metadata={{ 'hpdos.workspaceKey': 'alpha' }} name="New session" />
        </div>

        <SessionListItems>
          {#snippet item({ item, index })}
            {#if renderMode === 'item'}
              <SessionListItem
                {item}
                {index}
                class="custom-row"
                onSelect={() => selected = item.id}
              >
                {#snippet children({ item })}
                  <strong>{item.label}</strong>
                  <span>{item.metadata['hpdos.workspaceKey']}</span>
                  <SessionListDelete {item} />
                {/snippet}
              </SessionListItem>
            {:else}
              <SessionListItem
                {item}
                {index}
                onSelect={() => selected = item.id}
              >
                {#snippet children()}
                  <SessionListTitle />
                  <SessionListSubtitle />
                {/snippet}
              </SessionListItem>
            {/if}
          {/snippet}
        </SessionListItems>
      </SessionListRoot>
    </main>
  </div>
</section>

<style>
  .tutorial {
    display: grid;
    gap: 1rem;
    color: #e8e8ea;
    background: #111215;
    min-height: 520px;
    padding: 1rem;
  }

  .intro,
  .layout {
    max-width: 980px;
    width: 100%;
    margin: 0 auto;
  }

  .eyebrow {
    color: #d79b55;
    font-size: 0.8rem;
    text-transform: uppercase;
  }

  .layout {
    display: grid;
    grid-template-columns: 280px 1fr;
    gap: 1rem;
  }

  .guide,
  .preview {
    border: 1px solid #2c2f38;
    background: #17181d;
    padding: 1rem;
  }

  .guide {
    color: #b8bbc5;
  }

  :global([data-hpd-session-list]) {
    display: grid;
    gap: 0.5rem;
  }

  .toolbar {
    margin-bottom: 0.5rem;
  }

  :global([data-hpd-session-list-new]),
  :global([data-hpd-session-list-delete]) {
    padding: 0.5rem 0.75rem;
    color: #f4f4f5;
    background: #262830;
    border: 1px solid #3a3d47;
  }

  :global([data-hpd-session-list-item]) {
    display: grid;
    gap: 0.25rem;
    width: 100%;
    padding: 0.75rem;
    text-align: left;
    color: #f4f4f5;
    background: #202127;
    border: 1px solid #333640;
  }

  :global([data-hpd-session-list-item][data-selected]) {
    border-color: #d79b55;
  }

  :global([data-hpd-session-list-item-subtitle]) {
    color: #989da8;
    font-size: 0.8rem;
  }

  :global([data-hpd-session-list-item].custom-row) {
    grid-template-columns: 1fr auto;
    align-items: center;
  }
</style>
