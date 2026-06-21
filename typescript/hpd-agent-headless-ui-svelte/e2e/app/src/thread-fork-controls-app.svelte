<script lang="ts">
  import {
    getThreadBranchChoiceControlLabel,
    getThreadBranchChoiceControlsForMessage,
    type Message,
    type ThreadBranchNavigationSnapshot,
  } from '@hpd-research/hpd-agent-headless-ui';
  import type { ThreadForkGroup, ThreadForkGroupMember, ThreadRuntimeChild } from '@hpd-research/hpd-agent-client';

  const stamp = '2026-01-01T00:00:00.000Z';

  type BranchId = 'main' | 'fork-a' | 'fork-b' | 'fork-a-retry';

  const members = (ids: BranchId[]): ThreadForkGroupMember[] =>
    ids.map((threadId, index) => ({
      threadId,
      name: threadId,
      index,
      isSource: index === 0,
      messageCount: index + 1,
      createdAt: stamp,
      lastActivity: stamp,
    }));

  const topGroup: ThreadForkGroup = {
    id: 'main@m2',
    sourceThreadId: 'main',
    forkedAtMessageId: 'm2',
    forkedAtMessageIndex: 1,
    choiceMessageIndex: 2,
    members: members(['main', 'fork-a', 'fork-b']),
  };

  const nestedGroup: ThreadForkGroup = {
    id: 'fork-a@m4',
    sourceThreadId: 'fork-a',
    forkedAtMessageId: 'm4',
    forkedAtMessageIndex: 3,
    choiceMessageIndex: 4,
    members: members(['fork-a', 'fork-a-retry']),
  };

  const runtimeChild: ThreadRuntimeChild = {
    threadId: 'subagent-reviewer',
    parentSessionId: 's1',
    parentThreadId: 'main',
    name: 'Reviewer',
    kind: 'SubAgent',
    visibility: 'Hidden',
    subAgentName: 'Reviewer',
    messageCount: 1,
    createdAt: stamp,
    lastActivity: stamp,
  };

  let selected: BranchId = $state('fork-a');
  let selectionCount = $state(0);

  const transcripts: Record<BranchId, Message[]> = {
    main: [message('m1', 'Plan'), message('m2', 'Choose approach'), message('m3-main', 'Original answer')],
    'fork-a': [message('m1', 'Plan'), message('m2', 'Choose approach'), message('m3a', 'Fork A answer'), message('m4', 'Refine A')],
    'fork-b': [message('m1', 'Plan'), message('m2', 'Choose approach'), message('m3b', 'Fork B answer')],
    'fork-a-retry': [message('m1', 'Plan'), message('m2', 'Choose approach'), message('m3a', 'Fork A answer'), message('m4', 'Refine A'), message('m5', 'Retry output')],
  };

  let messages = $derived(transcripts[selected]);
  let navigation = $derived(createNavigation(selected));

  function message(id: string, content: string): Message {
    return {
      id,
      role: id === 'm1' || id === 'm2' || id === 'm4' ? 'user' : 'assistant',
      content,
      streaming: false,
      thinking: false,
      timestamp: new Date(stamp),
      toolCalls: [],
      turnId: null,
      conversationId: null,
      runId: null,
      placement: 'transcript',
    };
  }

  function createNavigation(threadId: BranchId): ThreadBranchNavigationSnapshot {
    const activePathChoices = threadId === 'main'
      ? [active(topGroup, 0)]
      : threadId === 'fork-a'
        ? [active(topGroup, 1), active(nestedGroup, 0)]
        : threadId === 'fork-b'
          ? [active(topGroup, 2)]
          : [active(topGroup, 1), active(nestedGroup, 1)];

    return {
      sessionId: 's1',
      threadId,
      graph: {
        threads: [],
        forkGroups: [topGroup, nestedGroup],
        runtimeChildren: [runtimeChild],
      },
      current: null,
      threads: [],
      forkGroups: [topGroup, nestedGroup],
      activePathChoices,
      runtimeChildren: [runtimeChild],
      hasRuntimeChildren: true,
    };
  }

  function active(group: ThreadForkGroup, activeIndex: number) {
    const selectedMember = group.members[activeIndex];
    return {
      group,
      selectedMember,
      selectedThreadId: selected,
      relationship: selectedMember.threadId === selected ? 'exact-member' : 'descendant-of-member',
      previous: group.members[activeIndex - 1] ?? null,
      next: group.members[activeIndex + 1] ?? null,
      position: {
        current: activeIndex + 1,
        total: group.members.length,
      },
    };
  }

  function select(threadId: string | undefined) {
    if (!threadId) return;
    selected = threadId as BranchId;
    selectionCount += 1;
  }

  function controlsForMessage(messageId: string) {
    return getThreadBranchChoiceControlsForMessage(navigation, messageId);
  }
</script>

<main>
  <h1>Thread fork controls e2e</h1>
  <p data-testid="selected-thread">{selected}</p>
  <p data-testid="selection-count">{selectionCount}</p>

  <section aria-label="messages">
    {#each messages as item (item.id)}
      <article class="message" data-testid={`message-${item.id}`}>
        <span>{item.content}</span>
        <div class="controls">
          {#each controlsForMessage(item.id) as control (control.groupId)}
            <div class="fork-control" data-testid={`fork-control-${control.groupId}`}>
              <button
                type="button"
                data-testid={`previous-${control.groupId}`}
                disabled={!control.previous}
                onclick={() => select(control.previous?.threadId)}
              >
                Previous
              </button>
              <span data-testid={`label-${control.groupId}`}>{getThreadBranchChoiceControlLabel(control)}</span>
              <button
                type="button"
                data-testid={`next-${control.groupId}`}
                disabled={!control.next}
                onclick={() => select(control.next?.threadId)}
              >
                Next
              </button>
            </div>
          {/each}
        </div>
      </article>
    {/each}
  </section>
</main>

<style>
  main {
    font-family: system-ui, sans-serif;
    margin: 24px;
    max-width: 760px;
  }

  .message {
    border-bottom: 1px solid #d5d8df;
    display: grid;
    gap: 8px;
    padding: 14px 0;
  }

  .controls {
    display: flex;
    gap: 8px;
  }

  .fork-control {
    align-items: center;
    display: inline-flex;
    gap: 8px;
  }
</style>
