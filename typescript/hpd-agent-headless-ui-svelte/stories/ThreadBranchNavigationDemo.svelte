<script lang="ts">
  import {
    createThreadBranchNavigationState,
    Message as MessageView,
    ThreadTimeline,
    ThreadBranchSwitcher,
    ThreadBranchSwitcherCount,
    ThreadBranchSwitcherNext,
    ThreadBranchSwitcherNumber,
    ThreadBranchSwitcherPrevious,
  } from '../src/index.js';
  import {
    getThreadBranchChoiceControlsByTimelineItem,
    type Message,
    type ThreadTimelineItem,
  } from '@hpd-research/hpd-agent-headless-ui';
  import type {
    AgentClient,
    Thread,
    ThreadGraph,
  } from '@hpd-research/hpd-agent-client';

  type Layout = 'timeline' | 'pager' | 'list' | 'tree';

  let {
    layout = 'timeline',
  }: {
    layout?: Layout;
  } = $props();

  let selectionLog = $state<string[]>([]);

  const navigation = createThreadBranchNavigationState({
    client: createStoryClient(),
    sessionId: 's1',
    threadId: 'main',
    onSelected: ({ trigger, groupId, threadId, previousThreadId }) => {
      selectionLog = [
        `${trigger}${groupId ? `:${groupId}` : ''}: ${previousThreadId} -> ${threadId}`,
        ...selectionLog,
      ].slice(0, 4);
    },
  });

  $effect(() => {
    void navigation.load();
  });

  const timeline = $derived(createTimeline($navigation.navigation.threadId));
  const projectedControls = $derived(
    [...getThreadBranchChoiceControlsByTimelineItem($navigation.navigation, timeline).values()].flat(),
  );
  const controlsByTimelineItem = $derived(
    getThreadBranchChoiceControlsByTimelineItem($navigation.navigation, timeline),
  );

  function createStoryClient(): AgentClient {
    const threads = [
      thread('main', {
        name: 'Original answer',
        childThreads: ['subagent-1', 'audit-1'],
        totalForks: 2,
      }),
      thread('edit-1', {
        name: 'Edited prompt',
        forkedFrom: 'main',
        forkedAtMessageId: 'm1',
        forkedAtMessageIndex: 0,
      }),
      thread('retry-1', {
        name: 'Retry with careful model',
        forkedFrom: 'main',
        forkedAtMessageId: 'm1',
        forkedAtMessageIndex: 0,
      }),
      thread('retry-2', {
        name: 'Retry with tools disabled',
        forkedFrom: 'main',
        forkedAtMessageId: 'm1',
        forkedAtMessageIndex: 0,
      }),
      thread('subagent-1', {
        name: 'Research subagent',
        kind: 'SubAgent',
        visibility: 'Hidden',
        parentThreadId: 'main',
        subAgentName: 'Researcher',
      }),
      thread('audit-1', {
        name: 'Audit worker',
        visibility: 'Hidden',
        parentThreadId: 'main',
      }),
    ];

    const graph: ThreadGraph = {
      threads,
      forkGroups: [{
        id: 'main@m1',
        sourceThreadId: 'main',
        forkedAtMessageId: 'm1',
        forkedAtMessageIndex: 0,
        choiceMessageIndex: 1,
        members: threads
          .filter((item) => ['main', 'edit-1', 'retry-1', 'retry-2'].includes(item.id))
          .map((item, index) => ({
            threadId: item.id,
            name: item.name ?? item.id,
            index,
            isSource: index === 0,
            choiceMessageId: getChoiceMessageId(item.id),
            choiceMessageIndex: 1,
            messageCount: item.messageCount,
            createdAt: item.createdAt,
            lastActivity: item.lastActivity,
          })),
      }],
      runtimeChildren: [
        {
          threadId: 'subagent-1',
          parentSessionId: 's1',
          parentThreadId: 'main',
          name: 'Research subagent',
          kind: 'SubAgent',
          visibility: 'Hidden',
          subAgentName: 'Researcher',
          messageCount: 6,
          createdAt: '2026-01-01T00:00:00.000Z',
          lastActivity: '2026-01-01T00:00:00.000Z',
        },
        {
          threadId: 'audit-1',
          parentSessionId: 's1',
          parentThreadId: 'main',
          name: 'Audit worker',
          kind: 'MainAgent',
          visibility: 'Hidden',
          messageCount: 6,
          createdAt: '2026-01-01T00:00:00.000Z',
          lastActivity: '2026-01-01T00:00:00.000Z',
        },
      ],
    };

    return {
      getThreadGraph: async () => graph,
    } as unknown as AgentClient;
  }

  function thread(id: string, overrides: Partial<Thread> = {}): Thread {
    return {
      id,
      sessionId: 's1',
      name: id,
      createdAt: '2026-01-01T00:00:00.000Z',
      lastActivity: '2026-01-01T00:00:00.000Z',
      messageCount: 6,
      kind: 'MainAgent',
      visibility: 'Visible',
      childThreads: [],
      totalForks: 0,
      ...overrides,
    };
  }

  function createTimeline(threadId: string): ThreadTimelineItem[] {
    const prefix = threadId || 'main';
    const branchTitle = getThreadTitle(prefix);
    const secondMessageId = getChoiceMessageId(prefix);
    return [
      timelineMessage('m1', 'user', 'Can you summarize the deployment risks?'),
      timelineMessage(secondMessageId, 'user', `${branchTitle}: focus the answer for this branch.`),
      timelineWork(`work-${prefix}`),
      timelineMessage(`${prefix}-m3`, 'assistant', `${branchTitle}: branch-specific answer with the selected tradeoffs.`),
    ];
  }

  function timelineMessage(id: string, role: Message['role'], content: string): ThreadTimelineItem {
    const itemMessage: Message = {
      id,
      role,
      content,
      contents: [{ type: 'text', text: content }],
      streaming: false,
      thinking: false,
      timestamp: new Date('2026-01-01T00:00:00.000Z'),
      toolCalls: [],
      turnId: null,
      conversationId: null,
      executionId: null,
      placement: 'transcript',
    };

    return {
      type: 'message',
      id: `timeline-${id}`,
      message: itemMessage,
      turnId: null,
      conversationId: null,
      executionId: null,
    };
  }

  function timelineWork(id: string): ThreadTimelineItem {
    return {
      type: 'work',
      id,
      work: {
        id,
        turnId: 'turn-1',
        conversationId: 'conversation-1',
        executionId: 'run-1',
        status: 'worked',
        label: 'Branch work completed',
        openByDefault: false,
        parts: [{
          type: 'progress',
          id: `${id}-progress`,
          label: 'Projected timeline keeps work rows separate from message anchors.',
        }],
      },
      turnId: 'turn-1',
      conversationId: 'conversation-1',
      executionId: 'run-1',
    };
  }

  function getChoiceMessageId(threadId: string): string {
    return threadId === 'main' ? 'main-m2' : `${threadId}-m2`;
  }

  function getThreadTitle(threadId: string): string {
    switch (threadId) {
      case 'main':
        return 'Original answer';
      case 'edit-1':
        return 'Edited prompt';
      case 'retry-1':
        return 'Retry with careful model';
      case 'retry-2':
        return 'Retry with tools disabled';
      default:
        return threadId;
    }
  }

</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Headless Svelte adapter</p>
    <h1>ThreadBranchNavigation tutorial playground</h1>
    <p>
      One graph-backed state can render branch controls as a pager, list, or
      tree without relying on thread-level sibling pointers.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>Current snapshot</h2>
      <dl>
        <div>
          <dt>Thread</dt>
          <dd>{$navigation.current?.name ?? 'Loading'}</dd>
        </div>
        <div>
          <dt>Fork groups</dt>
          <dd>{$navigation.forkGroups.length}</dd>
        </div>
        <div>
          <dt>Active labels</dt>
          <dd>{$navigation.activeLabels.join(', ') || 'None'}</dd>
        </div>
      </dl>

      {#if selectionLog.length > 0}
        <h2>Selection log</h2>
        <ol>
          {#each selectionLog as entry}
            <li>{entry}</li>
          {/each}
        </ol>
      {/if}
    </aside>

    <main class="preview">
      {#if layout === 'timeline'}
        <section class="panel timeline-panel">
          <h2>Inline timeline controls</h2>
          <ThreadTimeline timeline={timeline}>
            {#snippet message({ item, message })}
              {@const controls = controlsByTimelineItem.get(item.id) ?? []}
              <div class="message-row">
                <MessageView {message} />
                {#if controls.length > 0}
                  <div class="inline-branch-controls" aria-label="Message branch controls">
                    {#each controls as control (control.groupId)}
                      <ThreadBranchSwitcher
                        {control}
                        onSelect={({ control: selectedControl, threadId }) =>
                          navigation.selectForkGroupMember(selectedControl.groupId, threadId)}
                      />
                    {/each}
                  </div>
                {/if}
              </div>
            {/snippet}
          </ThreadTimeline>
        </section>
      {:else if layout === 'pager'}
        {@const control = projectedControls[0] ?? null}
        <section class="panel pager">
          {#if control}
            <ThreadBranchSwitcher
              {control}
              onSelect={({ control: selectedControl, threadId }) =>
                navigation.selectForkGroupMember(selectedControl.groupId, threadId)}
            />
            <div class="compact-switcher" aria-label="Compact branch switcher">
              <ThreadBranchSwitcherPrevious
                {control}
                onSelect={({ control: selectedControl, threadId }) =>
                  navigation.selectForkGroupMember(selectedControl.groupId, threadId)}
              />
              <strong>
                <ThreadBranchSwitcherNumber {control} />
                /
                <ThreadBranchSwitcherCount {control} />
              </strong>
              <ThreadBranchSwitcherNext
                {control}
                onSelect={({ control: selectedControl, threadId }) =>
                  navigation.selectForkGroupMember(selectedControl.groupId, threadId)}
              />
            </div>
          {:else}
            <strong>No group</strong>
          {/if}
        </section>
      {:else if layout === 'list'}
        <section class="panel">
          <h2>Fork groups</h2>
          {#each $navigation.forkGroups as group}
            <div class="branch-list">
              {#each group.members as member}
                <button
                  class:active={member.threadId === $navigation.navigation.threadId}
                  onclick={() => navigation.selectForkGroupMember(group.id, member.threadId)}
                >
                  <strong>{member.isSource ? 'Source' : `Fork ${member.index + 1}`}</strong>
                  <span>{member.name}</span>
                  <small>{member.messageCount} messages</small>
                </button>
              {/each}
            </div>
          {/each}
        </section>
      {:else}
        <section class="panel tree">
          <h2>Branch tree</h2>
          {#each $navigation.forkGroups as group}
            <div class="tree-row">
              {#each group.members as member}
                <button
                  class="tree-node"
                  class:root={member.isSource}
                  class:active={member.threadId === $navigation.navigation.threadId}
                  onclick={() => navigation.selectForkGroupMember(group.id, member.threadId)}
                >
                  {member.name}
                </button>
              {/each}
            </div>
          {/each}
          {#if $navigation.runtimeChildren.length > 0}
            <h3>Runtime children</h3>
            <div class="tree-row">
              {#each $navigation.runtimeChildren as child}
                <button class="tree-node child" onclick={() => navigation.selectThread(child.threadId)}>
                  {child.name}
                </button>
              {/each}
            </div>
          {/if}
        </section>
      {/if}
    </main>
  </div>
</section>

<style>
  .tutorial {
    min-height: 100%;
    padding: 28px;
    background: #f5f3ef;
    color: #232520;
    font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  .intro {
    max-width: 760px;
    margin-bottom: 24px;
  }

  .eyebrow {
    margin: 0 0 8px;
    font-size: 12px;
    font-weight: 700;
    text-transform: uppercase;
    color: #66705f;
  }

  h1, h2, h3, p {
    margin-top: 0;
  }

  .layout {
    display: grid;
    grid-template-columns: minmax(220px, 280px) minmax(0, 1fr);
    gap: 20px;
  }

  .guide,
  .panel {
    border: 1px solid #d8d4ca;
    border-radius: 8px;
    background: #fffdf8;
    padding: 18px;
  }

  dl div {
    display: flex;
    justify-content: space-between;
    gap: 16px;
    border-bottom: 1px solid #e5e0d6;
    padding: 8px 0;
  }

  dt {
    color: #66705f;
  }

  .pager {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 14px;
    min-height: 180px;
  }

  .timeline-panel {
    display: grid;
    gap: 14px;
  }

  .timeline-panel :global([data-hpd-thread-timeline]) {
    display: grid;
    gap: 12px;
  }

  .message-row {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto;
    align-items: start;
    gap: 12px;
  }

  .inline-branch-controls {
    display: flex;
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: 8px;
    min-width: 160px;
  }

  .timeline-panel :global([data-hpd-thread-work-group]) {
    border: 1px dashed #c9c3b8;
    border-radius: 7px;
    padding: 10px;
  }

  .compact-switcher,
  :global([data-hpd-thread-branch-switcher]) {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }

  button {
    border: 1px solid #c9c3b8;
    border-radius: 7px;
    background: #ffffff;
    color: #232520;
    padding: 10px 12px;
    cursor: pointer;
  }

  button:disabled {
    cursor: not-allowed;
    opacity: 0.45;
  }

  .branch-list {
    display: grid;
    gap: 8px;
  }

  .branch-list button {
    display: grid;
    grid-template-columns: 96px minmax(0, 1fr) auto;
    gap: 12px;
    text-align: left;
  }

  .active {
    border-color: #3b6f65;
    background: #edf7f3;
  }

  .tree-row {
    display: flex;
    flex-wrap: wrap;
    gap: 10px;
    margin-bottom: 14px;
  }

  .tree-node.root {
    font-weight: 700;
  }

  .tree-node.child {
    border-style: dashed;
  }

  @media (max-width: 760px) {
    .layout {
      grid-template-columns: 1fr;
    }

    .message-row {
      grid-template-columns: 1fr;
    }

    .inline-branch-controls {
      justify-content: flex-start;
    }
  }
</style>
