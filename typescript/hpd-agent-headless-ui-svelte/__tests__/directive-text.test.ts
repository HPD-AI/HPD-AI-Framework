import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it } from 'vitest';
import type { Message as ThreadMessage } from '@hpd-research/hpd-agent-headless-ui';
import DirectiveText from '../src/directive-text/directive-text.svelte';
import MessageParts from '../src/message/message-parts.svelte';

function message(overrides: Partial<ThreadMessage> = {}): ThreadMessage {
  return {
    id: 'm1',
    role: 'user',
    content: 'Ask @Workspace to run /deep',
    contents: [{ $type: 'text', text: 'Ask @Workspace to run /deep' }],
    streaming: false,
    thinking: false,
    timestamp: new Date('2026-01-01T00:00:00.000Z'),
    toolCalls: [],
    turnId: null,
    conversationId: null,
    executionId: null,
    placement: 'transcript',
    additionalProperties: {
      directives: [
        {
          id: 'workspace',
          label: 'Workspace',
          text: '@Workspace',
          trigger: '@',
          type: 'tool',
        },
        {
          id: 'deep',
          label: 'Deep',
          text: '/deep',
          trigger: '/',
          type: 'command',
        },
      ],
    },
    ...overrides,
  };
}

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

async function tick(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
  flushSync();
}

describe('DirectiveText', () => {
  it('renders structured directives as inline chips', async () => {
    const target = mountTarget();
    const component = mount(DirectiveText, {
      target,
      props: {
        message: message(),
        text: 'Ask @Workspace to run /deep',
      },
    });
    await tick();

    const chips = target.querySelectorAll('[data-hpd-directive-text-chip]');
    expect(chips).toHaveLength(2);
    expect(chips[0]?.textContent).toBe('@Workspace');
    expect(chips[0]?.getAttribute('data-directive-id')).toBe('workspace');
    expect(chips[1]?.textContent).toBe('/deep');
    expect(chips[1]?.getAttribute('data-directive-type')).toBe('command');

    await unmount(component);
    target.remove();
  });

  it('is the default message text renderer', async () => {
    const target = mountTarget();
    const component = mount(MessageParts, {
      target,
      props: {
        message: message(),
      },
    });
    await tick();

    expect(target.querySelector('[data-hpd-message-content]')).not.toBeNull();
    expect(target.querySelectorAll('[data-hpd-directive-text-chip]')).toHaveLength(2);

    await unmount(component);
    target.remove();
  });
});
