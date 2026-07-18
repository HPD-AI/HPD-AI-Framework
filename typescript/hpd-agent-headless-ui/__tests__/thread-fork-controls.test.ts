import { describe, expect, it } from 'vitest';
import {
  getThreadBranchChoiceControlLabel,
  getThreadBranchChoiceControlsByTimelineItem,
  getThreadBranchChoiceControlsForTimeline,
  type ActivePathChoice,
  type Message,
  type ThreadBranchNavigationSnapshot,
  type ThreadTimelineItem,
  type ThreadWorkGroup,
} from '../src/index.js';

const stamp = '2026-01-01T00:00:00.000Z';

function message(id: string, index: number): Message {
  return {
    id,
    role: index % 2 === 0 ? 'user' : 'assistant',
    content: id,
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

function timelineMessage(id: string, index: number): ThreadTimelineItem {
  const itemMessage = message(id, index);
  return {
    type: 'message',
    id: `message-item-${id}`,
    message: itemMessage,
    turnId: itemMessage.turnId,
    conversationId: itemMessage.conversationId,
    runId: itemMessage.runId,
  };
}

function timelineWork(id: string): ThreadTimelineItem {
  const work: ThreadWorkGroup = {
    id,
    turnId: 'turn-1',
    conversationId: 'conversation-1',
    runId: 'run-1',
    status: 'worked',
    label: 'Worked',
    openByDefault: false,
    parts: [],
  };
  return {
    type: 'work',
    id,
    work,
    turnId: work.turnId,
    conversationId: work.conversationId,
    runId: work.runId,
  };
}

function activeGroup(
  id: string,
  sourceThreadId: string,
  boundaryMessageId: string | null,
  boundaryMessageIndex: number | null,
  activeIndex: number,
  memberIds: string[],
  selectedThreadId = memberIds[activeIndex],
  choiceMessageIndex = boundaryMessageIndex == null ? 0 : boundaryMessageIndex + 1,
  memberChoiceMessages: Record<string, { id?: string; index?: number }> = {},
): ActivePathChoice {
  const defaultChoiceMessageId = `m${choiceMessageIndex + 1}`;
  const members = memberIds.map((threadId, index) => ({
    threadId,
    name: threadId,
    index,
    isSource: index === 0,
    choiceMessageId: memberChoiceMessages[threadId]?.id ?? defaultChoiceMessageId,
    choiceMessageIndex: memberChoiceMessages[threadId]?.index ?? choiceMessageIndex,
    messageCount: index + 1,
    createdAt: stamp,
    lastActivity: stamp,
  }));
  const selectedMember = members[activeIndex];
  return {
    group: {
      id,
      sourceThreadId,
      forkedAtMessageId: boundaryMessageId,
      forkedAtMessageIndex: boundaryMessageIndex,
      choiceMessageIndex,
      members,
    },
    selectedMember,
    selectedThreadId,
    relationship: selectedThreadId === selectedMember.threadId ? 'exact-member' : 'descendant-of-member',
    previous: members[activeIndex - 1] ?? null,
    next: members[activeIndex + 1] ?? null,
    position: {
      current: activeIndex + 1,
      total: members.length,
    },
  };
}

function navigation(activePathChoices: ActivePathChoice[]): ThreadBranchNavigationSnapshot {
  const forkGroups = activePathChoices.map((group) => group.group);
  return {
    sessionId: 's1',
    threadId: activePathChoices.at(-1)?.selectedMember.threadId ?? 'main',
    graph: {
      threads: [],
      forkGroups,
      runtimeChildren: [{
        threadId: 'subagent-1',
        sessionId: 's1',
        defaultAgentId: 'reviewer-agent',
        parentSessionId: 's1',
        parentThreadId: 'main',
        name: 'Reviewer',
        kind: 'SubAgent',
        visibility: 'Hidden',
        subAgentName: 'Reviewer',
        messageCount: 1,
        createdAt: stamp,
        lastActivity: stamp,
      }],
    },
    current: null,
    threads: [],
    forkGroups,
    activePathChoices,
    runtimeChildren: [],
    hasRuntimeChildren: false,
  };
}

describe('thread fork controls', () => {
  it('derives stable render controls from active path fork groups and first divergent timeline rows', () => {
    const nav = navigation([
      activeGroup('main@m2', 'main', 'm2', 1, 1, ['main', 'fork-a', 'fork-b'], 'fork-a-retry'),
      activeGroup('fork-a@m4', 'fork-a', 'm4', 3, 1, ['fork-a', 'fork-a-retry']),
    ]);

    const controls = getThreadBranchChoiceControlsForTimeline(nav, [
      timelineMessage('m1', 0),
      timelineMessage('m2', 1),
      timelineMessage('m3', 2),
      timelineMessage('m4', 3),
      timelineMessage('m5', 4),
    ]);

    expect(controls.map((control) => control.groupId)).toEqual(['main@m2', 'fork-a@m4']);
    expect(controls.map((control) => control.renderTimelineIndex)).toEqual([2, 4]);
    expect(controls.map((control) => control.renderTimelineItemId)).toEqual(['message-item-m3', 'message-item-m5']);
    expect(controls.map(getThreadBranchChoiceControlLabel)).toEqual(['Fork 2 / 3', 'Fork 2 / 2']);
    expect(controls[0].selectedThreadId).toBe('fork-a-retry');
    expect(controls[0].relationship).toBe('descendant-of-member');
    expect(controls[1].relationship).toBe('exact-member');
    expect(controls[0].previous?.threadId).toBe('main');
    expect(controls[0].next?.threadId).toBe('fork-b');
  });

  it('anchors timeline controls to message rows instead of transcript indexes', () => {
    const nav = navigation([
      activeGroup('main@m2', 'main', 'm1', 0, 1, ['main', 'fork-a']),
    ]);
    const timeline = [
      timelineMessage('m1', 0),
      timelineWork('work-1'),
      timelineMessage('m2', 1),
      timelineWork('work-2'),
    ];

    const controls = getThreadBranchChoiceControlsForTimeline(nav, timeline);

    expect(controls).toHaveLength(1);
    expect(controls[0].renderTimelineItemId).toBe('message-item-m2');
    expect(controls[0].renderTimelineIndex).toBe(2);
  });

  it('anchors to the explicit choice message index', () => {
    const nav = navigation([
      activeGroup(
        'main@m2',
        'main',
        'm2',
        1,
        1,
        ['main', 'fork-a'],
        undefined,
        2,
        { 'fork-a': { id: 'replacement', index: 2 } },
      ),
    ]);

    const controls = getThreadBranchChoiceControlsForTimeline(nav, [
      timelineMessage('m1', 0),
      timelineMessage('m2', 1),
      timelineMessage('replacement', 2),
    ]);

    expect(controls.map((control) => control.groupId)).toEqual(['main@m2']);
    expect(controls[0].renderTimelineItemId).toBe('message-item-replacement');
    expect(controls[0].renderTimelineIndex).toBe(2);
    expect(controls[0].renderPlacement).toBe('choice-message');
  });

  it('does not render a control when the explicit choice message is not in the timeline', () => {
    const nav = navigation([
      activeGroup(
        'main@m2',
        'main',
        'missing-boundary-id',
        1,
        1,
        ['main', 'fork-a'],
        undefined,
        99,
        { 'fork-a': { id: 'missing-choice-message', index: 99 } },
      ),
    ]);

    const controls = getThreadBranchChoiceControlsForTimeline(nav, [
      timelineMessage('m1', 0),
      timelineMessage('m2-copy-with-new-id', 1),
      timelineMessage('replacement', 2),
    ]);

    expect(controls).toEqual([]);
  });

  it('anchors root fork groups to the first visible timeline message', () => {
    const nav = navigation([
      activeGroup('main@root', 'main', null, null, 2, ['main', 'retry-root', 'edit-root']),
      activeGroup('main@m2', 'main', 'm2', 1, 1, ['main', 'fork-a']),
    ]);

    const controls = getThreadBranchChoiceControlsForTimeline(nav, [
      timelineMessage('m1', 0),
      timelineMessage('m2', 1),
      timelineMessage('m3', 2),
    ]);

    expect(controls.map((control) => control.groupId)).toEqual(['main@root', 'main@m2']);
    expect(controls[0].renderTimelineItemId).toBe('message-item-m1');
    expect(controls[0].renderTimelineIndex).toBe(0);
    expect(controls[0].boundaryMessageId).toBeNull();
    expect(getThreadBranchChoiceControlLabel(controls[0])).toBe('Fork 3 / 3');
  });

  it('keeps nested branch controls path scoped when switching to another top-level branch', () => {
    const forkA = navigation([
      activeGroup(
        'main@m2',
        'main',
        'm2',
        1,
        1,
        ['main', 'fork-a', 'fork-b'],
        'fork-a-retry',
        2,
        { 'fork-a': { id: 'm3b', index: 2 } },
      ),
      activeGroup('fork-a@m4', 'fork-a', 'm4', 3, 1, ['fork-a', 'fork-a-retry']),
    ]);
    const forkB = navigation([
      activeGroup(
        'main@m2',
        'main',
        'm2',
        1,
        2,
        ['main', 'fork-a', 'fork-b'],
        undefined,
        2,
        { 'fork-b': { id: 'm3b', index: 2 } },
      ),
    ]);
    const timeline = [timelineMessage('m1', 0), timelineMessage('m2', 1), timelineMessage('m3b', 2)];

    expect(getThreadBranchChoiceControlsForTimeline(forkA, timeline).map((control) => control.groupId))
      .toEqual(['main@m2']);
    expect(getThreadBranchChoiceControlsForTimeline(forkB, timeline).map((control) => control.groupId))
      .toEqual(['main@m2']);
    expect(getThreadBranchChoiceControlsForTimeline(forkA, timeline)[0].renderTimelineItemId)
      .toBe('message-item-m3b');
    expect(getThreadBranchChoiceControlsForTimeline(forkB, timeline)[0].position)
      .toEqual({ current: 3, total: 3 });
  });

  it('returns all controls for the same timeline message instead of assuming one control per row', () => {
    const nav = navigation([
      activeGroup('main@m2', 'main', 'm1', 0, 1, ['main', 'fork-a']),
      activeGroup('fork-a@m2-again', 'fork-a', 'm1', 0, 1, ['fork-a', 'fork-a-edit']),
    ]);

    const controls = getThreadBranchChoiceControlsForTimeline(nav, [
      timelineMessage('m1', 0),
      timelineMessage('m2', 1),
    ]);

    expect(controls.map((control) => control.groupId).sort()).toEqual(['fork-a@m2-again', 'main@m2']);
  });

  it('groups branch controls by rendered timeline item id', () => {
    const nav = navigation([
      activeGroup('main@m2', 'main', 'm1', 0, 1, ['main', 'fork-a']),
      activeGroup('fork-a@m2-again', 'fork-a', 'm1', 0, 1, ['fork-a', 'fork-a-edit']),
    ]);

    const controlsByItem = getThreadBranchChoiceControlsByTimelineItem(nav, [
      timelineMessage('m1', 0),
      timelineWork('work-1'),
      timelineMessage('m2', 1),
    ]);

    expect([...controlsByItem.keys()]).toEqual(['message-item-m2']);
    expect(controlsByItem.get('message-item-m2')?.map((control) => control.groupId).sort())
      .toEqual(['fork-a@m2-again', 'main@m2']);
    expect(controlsByItem.get('work-1')).toBeUndefined();
  });

  it('does not guess from the group index when the selected member has no render anchor', () => {
    const nav = navigation([
      activeGroup(
        'main@m2',
        'main',
        'm2',
        1,
        1,
        ['main', 'fork-a'],
        undefined,
        2,
        { 'fork-a': { id: undefined, index: undefined } },
      ),
    ]);
    const selectedMember = nav.activePathChoices[0].selectedMember;
    delete selectedMember.choiceMessageId;
    delete selectedMember.choiceMessageIndex;

    const controls = getThreadBranchChoiceControlsForTimeline(nav, [
      timelineMessage('m1', 0),
      timelineMessage('m2', 1),
      timelineMessage('m3', 2),
    ]);

    expect(controls).toEqual([]);
  });
});
