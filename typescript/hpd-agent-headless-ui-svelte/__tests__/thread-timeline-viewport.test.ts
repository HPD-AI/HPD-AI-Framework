import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import type {
  Message,
  ThreadProjectionSnapshot,
  ThreadTimelineItem,
} from '@hpd-research/hpd-agent-headless-ui';
import type {
  ThreadState,
  ThreadStateSnapshot,
} from '../src/index.js';
import ThreadTimelineViewport from '../src/thread-timeline-viewport/thread-timeline-viewport.svelte';
import ThreadTimelineViewportCustomHarness from './fixtures/thread-timeline-viewport-custom-harness.svelte';
import ThreadTimelineViewportPrimitivesHarness from './fixtures/thread-timeline-viewport-primitives-harness.svelte';

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

function projection(overrides: Partial<ThreadProjectionSnapshot> = {}): ThreadProjectionSnapshot {
  return {
    thread: null,
    timeline: [],
    workGroups: [],
    transcriptMessages: [],
    activeTools: [],
    pendingRuntimeRequests: [],
    threadRun: null,
    activity: {
      status: 'idle',
      streaming: false,
      reasoning: false,
      activeToolCount: 0,
      pendingRequestCount: 0,
    },
    currentTurnId: null,
    currentConversationId: null,
    currentRunId: null,
    error: null,
    canSend: true,
    ...overrides,
  };
}

function snapshot(overrides: Partial<ThreadStateSnapshot> = {}): ThreadStateSnapshot {
  const projected = projection({
    timeline: overrides.timeline ?? [],
  });

  return {
    projection: projected,
    timeline: projected.timeline,
    workGroups: [],
    transcriptMessages: [],
    activity: projected.activity,
    activeTools: [],
    pendingRuntimeRequests: [],
    textSubmissionState: { canSubmit: true, reason: null },
    canSubmitText: true,
    loading: false,
    connected: true,
    error: null,
    ...overrides,
  };
}

function fakeThread(initialSnapshot: ThreadStateSnapshot = snapshot()): ThreadState & {
  emit(nextSnapshot: ThreadStateSnapshot): void;
} {
  let current = initialSnapshot;
  const subscribers = new Set<(value: ThreadStateSnapshot) => void>();
  const thread = {
    controller: {} as ThreadState['controller'],
    subscribe(run: (value: ThreadStateSnapshot) => void) {
      subscribers.add(run);
      run(current);
      return () => {
        subscribers.delete(run);
      };
    },
    getSnapshot: () => current,
    clearError: () => {},
    start: vi.fn(async () => {}),
    rehydrate: vi.fn(async () => {}),
    connect: vi.fn(async () => {}),
    disconnect: vi.fn(async () => {}),
    dispose: vi.fn(async () => {}),
    sendMessage: vi.fn(async () => {}),
    run: vi.fn(async () => undefined),
    respond: vi.fn(async () => undefined),
    interrupt: vi.fn(async () => {}),
    approve: vi.fn(async () => undefined),
    deny: vi.fn(async () => undefined),
    clarify: vi.fn(async () => undefined),
    answerClientToolRequest: vi.fn(async () => undefined),
    emit(nextSnapshot: ThreadStateSnapshot) {
      current = nextSnapshot;
      for (const subscriber of subscribers) subscriber(current);
      flushSync();
    },
  };

  return thread as ThreadState & { emit(nextSnapshot: ThreadStateSnapshot): void };
}

async function settle(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
  await Promise.resolve();
  flushSync();
}

function defineScrollMetrics(
  node: HTMLElement,
  metrics: {
    clientHeight: () => number;
    scrollHeight: () => number;
  },
): void {
  Object.defineProperty(node, 'clientHeight', {
    configurable: true,
    get: metrics.clientHeight,
  });
  Object.defineProperty(node, 'scrollHeight', {
    configurable: true,
    get: metrics.scrollHeight,
  });
}

function createTimeline(messages: Partial<Message>[]): ThreadTimelineItem[] {
  return messages.map((message, index) => {
    const resolved = createMessage({
      id: `message-${index + 1}`,
      role: index % 2 === 0 ? 'user' : 'assistant',
      content: `message ${index + 1}`,
      ...message,
    });

    return {
      type: 'message',
      id: `timeline-${resolved.id}`,
      message: resolved,
      turnId: resolved.turnId,
      conversationId: resolved.conversationId,
      runId: resolved.runId,
    };
  });
}

function createMessage(overrides: Partial<Message> = {}): Message {
  return {
    id: 'message-1',
    role: 'user',
    content: 'hello',
    streaming: false,
    thinking: false,
    timestamp: new Date('2026-01-01T00:00:00.000Z'),
    toolCalls: [],
    turnId: null,
    conversationId: null,
    runId: null,
    placement: 'transcript',
    ...overrides,
  };
}

describe('ThreadTimelineViewport', () => {
  it('renders ThreadTimeline by default with viewport attributes', async () => {
    const target = mountTarget();
    const component = mount(ThreadTimelineViewport, {
      target,
      props: {
        timeline: createTimeline([{ content: 'Hello viewport' }]),
      },
    });

    const viewport = target.querySelector('[data-hpd-thread-timeline-viewport]');
    expect(viewport).not.toBeNull();
    expect(viewport?.getAttribute('role')).toBe('log');
    expect(viewport?.getAttribute('data-auto-scroll')).toBe('true');
    expect(viewport?.getAttribute('data-turn-anchor')).toBe('top');
    expect(target.querySelector('[data-hpd-message]')?.textContent).toContain('Hello viewport');

    await unmount(component);
    target.remove();
  });

  it('lets callers replace inner rendering while keeping the viewport API', async () => {
    const target = mountTarget();
    const component = mount(ThreadTimelineViewportCustomHarness, {
      target,
      props: {
        timeline: createTimeline([{ id: 'user-1' }, { id: 'assistant-1', role: 'assistant' }]),
      },
    });
    await settle();

    const viewport = target.querySelector('[data-hpd-thread-timeline-viewport]') as HTMLElement;
    let scrollHeight = 500;
    defineScrollMetrics(viewport, {
      clientHeight: () => 100,
      scrollHeight: () => scrollHeight,
    });
    viewport.scrollTop = 100;
    target.querySelector<HTMLButtonElement>('[data-testid="jump"]')?.click();

    expect(viewport.scrollTop).toBe(scrollHeight);
    expect(target.querySelectorAll('[data-testid="custom-item"]')).toHaveLength(2);

    await unmount(component);
    target.remove();
  });

  it('keeps bottom pinned while timeline content changes near the bottom', async () => {
    const first = createTimeline([{ id: 'user-1' }, { id: 'assistant-1', role: 'assistant' }]);
    const thread = fakeThread(snapshot({ timeline: first }));
    const target = mountTarget();
    const component = mount(ThreadTimelineViewport, {
      target,
      props: {
        autoScroll: true,
        turnAnchor: 'bottom',
        thread,
      },
    });
    await settle();

    const viewport = target.querySelector('[data-hpd-thread-timeline-viewport]') as HTMLElement;
    let scrollHeight = 500;
    defineScrollMetrics(viewport, {
      clientHeight: () => 100,
      scrollHeight: () => scrollHeight,
    });
    viewport.scrollTop = 400;
    viewport.dispatchEvent(new Event('scroll'));
    scrollHeight = 700;

    thread.emit(snapshot({
      timeline: createTimeline([
        { id: 'user-1' },
        { id: 'assistant-1', role: 'assistant', content: 'assistant streaming more text' },
      ]),
    }));
    await settle();

    expect(viewport.scrollTop).toBe(700);

    await unmount(component);
    target.remove();
  });

  it('suppresses automatic bottom scrolling after the user scrolls away', async () => {
    const first = createTimeline([{ id: 'user-1' }, { id: 'assistant-1', role: 'assistant' }]);
    const thread = fakeThread(snapshot({ timeline: first }));
    const target = mountTarget();
    const component = mount(ThreadTimelineViewport, {
      target,
      props: {
        autoScroll: true,
        turnAnchor: 'bottom',
        thread,
      },
    });
    await settle();

    const viewport = target.querySelector('[data-hpd-thread-timeline-viewport]') as HTMLElement;
    let scrollHeight = 500;
    defineScrollMetrics(viewport, {
      clientHeight: () => 100,
      scrollHeight: () => scrollHeight,
    });
    viewport.scrollTop = 100;
    viewport.dispatchEvent(new Event('scroll'));
    flushSync();
    scrollHeight = 700;

    thread.emit(snapshot({
      timeline: createTimeline([
        { id: 'user-1' },
        { id: 'assistant-1', role: 'assistant', content: 'assistant streaming more text' },
      ]),
    }));
    await settle();

    expect(viewport.scrollTop).toBe(100);
    expect(viewport.getAttribute('data-auto-scroll-suppressed')).toBe('');

    await unmount(component);
    target.remove();
  });

  it('anchors a newly sent user message with the top turn anchor', async () => {
    const originalRect = HTMLElement.prototype.getBoundingClientRect;
    HTMLElement.prototype.getBoundingClientRect = function getBoundingClientRect() {
      if (this.hasAttribute('data-hpd-thread-timeline-viewport')) {
        return { top: 10, left: 0, right: 0, bottom: 210, width: 0, height: 200, x: 0, y: 10, toJSON: () => ({}) };
      }
      if (this.getAttribute('data-message-id') === 'user-2') {
        return { top: 160, left: 0, right: 0, bottom: 200, width: 0, height: 40, x: 0, y: 160, toJSON: () => ({}) };
      }
      return originalRect.call(this);
    };

    try {
      const thread = fakeThread(snapshot({
        timeline: createTimeline([{ id: 'user-1' }, { id: 'assistant-1', role: 'assistant' }]),
      }));
      const target = mountTarget();
      const component = mount(ThreadTimelineViewport, {
        target,
        props: {
          autoScroll: true,
          thread,
          turnAnchor: 'top',
        },
      });
      await settle();

      const viewport = target.querySelector('[data-hpd-thread-timeline-viewport]') as HTMLElement;
      defineScrollMetrics(viewport, {
        clientHeight: () => 100,
        scrollHeight: () => 800,
      });
      viewport.scrollTop = 25;
      viewport.dispatchEvent(new Event('scroll'));

      thread.emit(snapshot({
        timeline: createTimeline([
          { id: 'user-1' },
          { id: 'assistant-1', role: 'assistant' },
          { id: 'user-2', role: 'user', content: 'new input' },
        ]),
      }));
      await settle();

      expect(viewport.scrollTop).toBe(175);

      await unmount(component);
      target.remove();
    } finally {
      HTMLElement.prototype.getBoundingClientRect = originalRect;
    }
  });

  it('disables automatic scrolling when requested', async () => {
    const thread = fakeThread(snapshot({
      timeline: createTimeline([{ id: 'user-1' }]),
    }));
    const target = mountTarget();
    const component = mount(ThreadTimelineViewport, {
      target,
      props: {
        autoScroll: false,
        thread,
      },
    });
    await settle();

    const viewport = target.querySelector('[data-hpd-thread-timeline-viewport]') as HTMLElement;
    defineScrollMetrics(viewport, {
      clientHeight: () => 100,
      scrollHeight: () => 800,
    });
    viewport.scrollTop = 25;

    thread.emit(snapshot({
      timeline: createTimeline([
        { id: 'user-1' },
        { id: 'assistant-1', role: 'assistant', content: 'new content' },
      ]),
    }));
    await settle();

    expect(viewport.scrollTop).toBe(25);
    expect(viewport.getAttribute('data-auto-scroll')).toBe('false');

    await unmount(component);
    target.remove();
  });

  it('exposes scroll-to-bottom and footer primitives through viewport context', async () => {
    const originalGetBoundingClientRect = HTMLElement.prototype.getBoundingClientRect;
    HTMLElement.prototype.getBoundingClientRect = function getBoundingClientRect() {
      if (this.hasAttribute('data-hpd-thread-timeline-viewport-footer')) {
        return { top: 0, left: 0, right: 0, bottom: 48, width: 0, height: 48, x: 0, y: 0, toJSON: () => ({}) };
      }
      return originalGetBoundingClientRect.call(this);
    };

    const target = mountTarget();
    try {
      const component = mount(ThreadTimelineViewportPrimitivesHarness, {
        target,
        props: {
          timeline: createTimeline([{ id: 'user-1' }, { id: 'assistant-1', role: 'assistant' }]),
        },
      });
      await settle();

      const viewport = target.querySelector('[data-hpd-thread-timeline-viewport]') as HTMLElement;
      const scrollHeight = 600;
      defineScrollMetrics(viewport, {
        clientHeight: () => 100,
        scrollHeight: () => scrollHeight,
      });
      viewport.scrollTop = 100;
      viewport.dispatchEvent(new Event('scroll'));
      await settle();

      const buttonNode = target.querySelector('[data-hpd-thread-scroll-to-bottom]') as HTMLButtonElement;
      expect(buttonNode.disabled).toBe(false);

      buttonNode.click();
      expect(viewport.scrollTop).toBe(scrollHeight - 48);

      await unmount(component);
      target.remove();
    } finally {
      HTMLElement.prototype.getBoundingClientRect = originalGetBoundingClientRect;
    }
  });
});
