import { afterEach, describe, expect, it, vi } from 'vitest';
import { mount, unmount } from 'svelte';
import {
  ThreadBranchSwitcher,
  ThreadBranchSwitcherCount,
  ThreadBranchSwitcherNext,
  ThreadBranchSwitcherNumber,
  ThreadBranchSwitcherPrevious,
} from '../src/index.js';
import type { ThreadBranchChoiceControl } from '@hpd-research/hpd-agent-headless-ui';

const mounted: Array<Record<string, unknown>> = [];

afterEach(() => {
  for (const component of mounted.splice(0)) {
    unmount(component);
  }
  document.body.innerHTML = '';
});

describe('ThreadBranchSwitcher', () => {
  it('renders branch position and selects previous/next members', () => {
    const onSelect = vi.fn();
    mounted.push(mount(ThreadBranchSwitcher, {
      target: document.body,
      props: {
        control: createControl(),
        onSelect,
      },
    }));

    const switcher = document.querySelector('[data-hpd-thread-branch-switcher]');
    expect(switcher?.getAttribute('data-group-id')).toBe('main@m1');
    expect(switcher?.textContent).toContain('Fork 2 / 3');

    const previous = document.querySelector('[data-direction="previous"]') as HTMLButtonElement;
    const next = document.querySelector('[data-direction="next"]') as HTMLButtonElement;
    previous.click();
    next.click();

    expect(onSelect).toHaveBeenNthCalledWith(1, expect.objectContaining({
      direction: 'previous',
      threadId: 'main',
    }));
    expect(onSelect).toHaveBeenNthCalledWith(2, expect.objectContaining({
      direction: 'next',
      threadId: 'fork-b',
    }));
  });

  it('renders nothing for single branch controls', () => {
    mounted.push(mount(ThreadBranchSwitcher, {
      target: document.body,
      props: {
        control: createControl(['main'], 0),
      },
    }));

    expect(document.querySelector('[data-hpd-thread-branch-switcher]')).toBeNull();
  });

  it('supports explicit leaf composition', () => {
    const onSelect = vi.fn();
    const control = createControl();
    mounted.push(mount(ThreadBranchSwitcherPrevious, {
      target: document.body,
      props: { control, onSelect },
    }));
    mounted.push(mount(ThreadBranchSwitcherNumber, {
      target: document.body,
      props: { control },
    }));
    mounted.push(mount(ThreadBranchSwitcherCount, {
      target: document.body,
      props: { control },
    }));
    mounted.push(mount(ThreadBranchSwitcherNext, {
      target: document.body,
      props: { control, onSelect },
    }));

    expect(document.querySelector('[data-hpd-thread-branch-switcher-number]')?.textContent).toBe('2');
    expect(document.querySelector('[data-hpd-thread-branch-switcher-count]')?.textContent).toBe('3');

    const previous = document.querySelector('[data-direction="previous"]') as HTMLButtonElement;
    const next = document.querySelector('[data-direction="next"]') as HTMLButtonElement;
    previous.click();
    next.click();

    expect(onSelect).toHaveBeenNthCalledWith(1, expect.objectContaining({
      direction: 'previous',
      threadId: 'main',
    }));
    expect(onSelect).toHaveBeenNthCalledWith(2, expect.objectContaining({
      direction: 'next',
      threadId: 'fork-b',
    }));
  });
});

function createControl(threadIds = ['main', 'fork-a', 'fork-b'], selectedIndex = 1): ThreadBranchChoiceControl {
  const members = threadIds.map((threadId, index) => ({
    threadId,
    name: threadId,
    index,
    isSource: index === 0,
    messageCount: index + 1,
    createdAt: '2026-01-01T00:00:00.000Z',
    lastActivity: '2026-01-01T00:00:00.000Z',
  }));
  return {
    groupId: 'main@m1',
    sourceThreadId: 'main',
    boundaryMessageId: 'm1',
    boundaryMessageIndex: 0,
    choiceMessageIndex: 1,
    renderTimelineItemId: 'message-item-m1',
    renderTimelineIndex: 0,
    renderPlacement: 'choice-message',
    selectedMember: members[selectedIndex],
    selectedThreadId: members[selectedIndex].threadId,
    relationship: 'exact-member',
    members,
    position: {
      current: selectedIndex + 1,
      total: members.length,
    },
    previous: members[selectedIndex - 1] ?? null,
    next: members[selectedIndex + 1] ?? null,
  };
}
