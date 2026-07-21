import { afterEach, describe, expect, it } from 'vitest';
import { mount, unmount } from 'svelte';
import type {
  Message,
  RuntimeRequest,
  ThreadTimelineItem,
  ThreadWorkGroup,
  ToolCall,
} from '@hpd-research/hpd-agent-headless-ui';
import ThreadTimeline from '../src/thread-timeline/thread-timeline.svelte';
import ThreadWorkGroup from '../src/thread-work-group/thread-work-group.svelte';
import ThreadWorkParts from '../src/thread-work-group/thread-work-parts.svelte';
import ThreadTimelineCustomHarness from './fixtures/thread-timeline-custom-harness.svelte';
import ThreadWorkGroupCustomHarness from './fixtures/thread-work-group-custom-harness.svelte';

const mounted: Array<Record<string, unknown>> = [];

afterEach(() => {
  for (const component of mounted.splice(0)) {
    unmount(component);
  }
  document.body.innerHTML = '';
});

describe('ThreadTimeline', () => {
  it('renders message, work, and runtime request timeline items', () => {
    mounted.push(mount(ThreadTimeline, {
      target: document.body,
      props: {
        timeline: createTimeline(),
      },
    }));

    expect(document.querySelector('[data-hpd-thread-timeline]')).toBeTruthy();
    expect(document.querySelector('[data-hpd-message]')?.textContent).toContain('Hello');
    expect(document.querySelector('[data-hpd-thread-work-group]')?.textContent).toContain('Working');
    expect(document.querySelector('[data-hpd-runtime-request]')?.textContent).toContain('permission');
  });

  it('lets callers replace item rendering with snippets', () => {
    mounted.push(mount(ThreadTimelineCustomHarness, {
      target: document.body,
      props: {
        timeline: createTimeline(),
      },
    }));

    expect(document.querySelector('[data-testid="custom-message"]')?.textContent).toContain('user: Hello');
    expect(document.querySelector('[data-testid="custom-work"]')?.textContent).toContain('Working (2)');
    expect(document.querySelector('[data-testid="custom-request"]')?.textContent).toContain('permission:req-1');
  });

  it('marks an empty timeline', () => {
    mounted.push(mount(ThreadTimeline, {
      target: document.body,
      props: {
        timeline: [],
      },
    }));

    expect(document.querySelector('[data-hpd-thread-timeline]')?.getAttribute('data-empty')).toBe('');
  });
});

describe('ThreadWorkGroup', () => {
  it('renders grouped work parts by default', () => {
    mounted.push(mount(ThreadWorkGroup, {
      target: document.body,
      props: {
        work: createWorkGroup(),
      },
    }));

    expect(document.querySelector('[data-hpd-thread-work-group]')?.getAttribute('data-work-status')).toBe('working');
    expect(document.querySelector('[data-work-part-type="reasoning"]')?.textContent).toContain('Checking context');
    expect(document.querySelector('[data-work-part-type="tool"]')?.textContent).toContain('read_file');
  });

  it('lets callers replace part rendering', () => {
    mounted.push(mount(ThreadWorkGroupCustomHarness, {
      target: document.body,
      props: {
        work: createWorkGroup(),
      },
    }));

    expect(document.querySelectorAll('[data-testid="custom-part"]')).toHaveLength(2);
    expect(document.querySelector('[data-testid="custom-part"]')?.getAttribute('data-part-type')).toBe('reasoning');
  });

  it('renders completed collapsed work while keeping tool history inspectable', () => {
    mounted.push(mount(ThreadWorkGroup, {
      target: document.body,
      props: {
        work: createWorkGroup({
          openByDefault: false,
          status: 'worked',
        }),
      },
    }));

    const group = document.querySelector('[data-hpd-thread-work-group]');
    expect(group?.getAttribute('data-work-status')).toBe('worked');
    expect(group?.hasAttribute('open')).toBe(false);
    expect(document.querySelector('[data-work-part-type="tool"]')?.textContent).toContain('read_file');
  });

  it('hides the promoted final assistant draft by default', () => {
    mounted.push(mount(ThreadWorkGroup, {
      target: document.body,
      props: {
        work: createWorkGroup({
          status: 'worked',
          finalMessageId: 'draft-1',
          parts: [{
            type: 'assistant-draft',
            id: 'draft-1',
            message: createMessage({
              id: 'draft-1',
              role: 'assistant',
              content: 'Final answer',
              placement: 'final',
            }),
          }],
        }),
      },
    }));

    expect(document.querySelector('[data-work-part-type="assistant-draft"]')).toBeNull();
  });

  it('can show the promoted final assistant draft when requested', () => {
    mounted.push(mount(ThreadWorkGroup, {
      target: document.body,
      props: {
        showFinalDraft: true,
        work: createWorkGroup({
          status: 'worked',
          finalMessageId: 'draft-1',
          parts: [{
            type: 'assistant-draft',
            id: 'draft-1',
            message: createMessage({
              id: 'draft-1',
              role: 'assistant',
              content: 'Final answer',
              placement: 'final',
            }),
          }],
        }),
      },
    }));

    expect(document.querySelector('[data-work-part-type="assistant-draft"]')?.textContent)
      .toContain('Final answer');
  });
});

describe('ThreadWorkParts', () => {
  it('renders structured work parts without the work group shell', () => {
    mounted.push(mount(ThreadWorkParts, {
      target: document.body,
      props: {
        work: createWorkGroup(),
      },
    }));

    expect(document.querySelector('[data-hpd-thread-work-group]')).toBeNull();
    expect(document.querySelector('[data-hpd-thread-work-parts]')).toBeTruthy();
    expect(document.querySelector('[data-work-part-type="reasoning"]')?.textContent)
      .toContain('Checking context');
    expect(document.querySelector('[data-work-part-type="tool"]')?.getAttribute('data-tool-id'))
      .toBe('tool-call-1');
  });

  it('marks empty structured work parts', () => {
    mounted.push(mount(ThreadWorkParts, {
      target: document.body,
      props: {
        work: createWorkGroup({ parts: [] }),
      },
    }));

    expect(document.querySelector('[data-hpd-thread-work-parts]')?.getAttribute('data-empty'))
      .toBe('');
  });
});

function createTimeline(): ThreadTimelineItem[] {
  const work = createWorkGroup();
  const request = createRuntimeRequest();
  return [
    {
      type: 'message',
      id: 'message-item-1',
      message: createMessage(),
      turnId: null,
      conversationId: null,
      executionId: null,
    },
    {
      type: 'work',
      id: 'work-item-1',
      work,
      turnId: work.turnId,
      conversationId: work.conversationId,
      executionId: work.executionId,
    },
    {
      type: 'runtime-request',
      id: 'request-item-1',
      request,
      turnId: 'turn-1',
      conversationId: 'conversation-1',
      executionId: 'run-1',
    },
  ];
}

function createMessage(overrides: Partial<Message> = {}): Message {
  return {
    id: 'message-1',
    role: 'user',
    content: 'Hello',
    streaming: false,
    thinking: false,
    timestamp: new Date('2026-01-01T00:00:00.000Z'),
    toolCalls: [],
    turnId: null,
    conversationId: null,
    executionId: null,
    placement: 'transcript',
    ...overrides,
  };
}

function createWorkGroup(overrides: Partial<ThreadWorkGroup> = {}): ThreadWorkGroup {
  const tool = createToolCall();
  return {
    id: 'work-1',
    turnId: 'turn-1',
    conversationId: 'conversation-1',
    executionId: 'run-1',
    status: 'working',
    label: 'Working',
    openByDefault: true,
    parts: [
      {
        type: 'reasoning',
        id: 'reasoning-1',
        messageId: 'draft-1',
        text: 'Checking context',
        status: 'streaming',
      },
      {
        type: 'tool',
        id: 'tool-1',
        tool,
      },
    ],
    ...overrides,
  };
}

function createToolCall(): ToolCall {
  return {
    callId: 'tool-call-1',
    name: 'read_file',
    messageId: 'draft-1',
    status: 'executing',
    startTime: new Date('2026-01-01T00:00:01.000Z'),
    args: { path: 'README.md' },
    turnId: 'turn-1',
    conversationId: 'conversation-1',
    executionId: 'run-1',
  };
}

function createRuntimeRequest(): RuntimeRequest {
  return {
    id: 'req-1',
    kind: 'permission',
    sourceName: 'toolharness',
    requestEventType: 'PERMISSION_REQUEST',
    expectedResponseEventType: 'PERMISSION_RESPONSE',
    request: {
      permissionId: 'req-1',
      sourceName: 'toolharness',
      functionName: 'write_file',
      callId: 'call-1',
      description: 'Write file?',
    },
  };
}
