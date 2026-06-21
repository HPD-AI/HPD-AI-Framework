import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import type { AgentClient, Session } from '@hpd-research/hpd-agent-client';
import {
  createSessionListDeleteElementProps,
  createSessionListNewElementProps,
  createSessionListRootElementProps,
  createSessionListItemElementProps,
  createSessionListState,
} from '../src/index.js';
import SessionListActionsHarness from './fixtures/session-list-actions-harness.svelte';
import SessionListCustomHarness from './fixtures/session-list-custom-harness.svelte';
import SessionListHarness from './fixtures/session-list-harness.svelte';

const stamp = '2026-01-01T00:00:00.000Z';

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

function session(id: string, metadata: Record<string, unknown> = {}): Session {
  return {
    id,
    createdAt: stamp,
    lastActivity: stamp,
    metadata,
  };
}

function fakeClient(sessions: Session[]): AgentClient {
  let current = [...sessions];
  return {
    searchSessions: vi.fn(async (request = {}) => {
      const filter = request.metadata ?? {};
      return current.filter((item) =>
        Object.entries(filter).every(([key, value]) => item.metadata?.[key] === value));
    }),
    createSession: vi.fn(async (request = {}) => {
      const created = session(request.sessionId ?? 'created', request.metadata ?? {});
      current = [created, ...current];
      return created;
    }),
    updateSession: vi.fn(async (sessionId, request) => {
      const existing = current.find((item) => item.id === sessionId);
      if (!existing) throw new Error('missing');
      const metadata = { ...existing.metadata };
      for (const [key, value] of Object.entries(request.metadata)) {
        if (value === null) delete metadata[key];
        else metadata[key] = value;
      }
      const updated = { ...existing, metadata };
      current = current.map((item) => item.id === sessionId ? updated : item);
      return updated;
    }),
    deleteSession: vi.fn(async (sessionId) => {
      current = current.filter((item) => item.id !== sessionId);
    }),
  } as unknown as AgentClient;
}

describe('SessionList', () => {
  it('creates root and item element props', () => {
    const snapshot = {
      sessions: [],
      items: [],
      selectedSession: null,
      selectedSessionId: null,
      loading: true,
      error: null,
      search: {},
      empty: true,
    };

    expect(createSessionListRootElementProps(snapshot)).toMatchObject({
      'aria-busy': true,
      'data-empty': '',
      'data-hpd-session-list': '',
      'data-loading': '',
    });

    expect(createSessionListItemElementProps({
      id: 's1',
      label: 'Session',
      metadata: {},
      selected: true,
      session: session('s1'),
      subtitle: null,
    })).toMatchObject({
      'aria-current': 'true',
      'data-hpd-session-list-item': '',
      'data-selected': '',
      'data-session-id': 's1',
      disabled: false,
      type: 'button',
    });

    expect(createSessionListNewElementProps(snapshot)).toMatchObject({
      'data-hpd-session-list-new': '',
      disabled: true,
      type: 'button',
    });

    expect(createSessionListDeleteElementProps(null, snapshot)).toMatchObject({
      'data-hpd-session-list-delete': '',
      disabled: true,
      type: 'button',
    });
  });

  it('renders sessions and selects a row', async () => {
    const target = mountTarget();
    const sessionList = createSessionListState({
      client: fakeClient([
        session('main', { name: 'Main' }),
        session('workspace', { name: 'Workspace' }),
      ]),
    });
    await sessionList.load();
    const onSelect = vi.fn();

    const component = mount(SessionListHarness, {
      target,
      props: { sessionList, onSelect },
    });

    const buttons = target.querySelectorAll<HTMLButtonElement>('[data-hpd-session-list-item]');
    expect(buttons).toHaveLength(2);
    expect(buttons[0].textContent).toContain('Main');

    buttons[1].click();
    await Promise.resolve();
    flushSync();

    expect(sessionList.getSnapshot().selectedSessionId).toBe('workspace');
    expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ id: 'workspace' }));

    await unmount(component);
    target.remove();
  });

  it('supports custom item snippets with metadata access', async () => {
    const target = mountTarget();
    const sessionList = createSessionListState({
      client: fakeClient([
        session('one', {
          name: 'One',
          'hpdos.workspaceKey': 'alpha',
        }),
      ]),
    });
    await sessionList.load();

    const component = mount(SessionListCustomHarness, {
      target,
      props: { sessionList },
    });

    expect(target.querySelector('[data-testid="custom-one"]')?.textContent)
      .toContain('One:alpha');

    await unmount(component);
    target.remove();
  });

  it('creates and deletes sessions through primitive controls', async () => {
    const target = mountTarget();
    const sessionList = createSessionListState({
      client: fakeClient([
        session('main', { name: 'Main' }),
      ]),
    });
    await sessionList.load();

    const component = mount(SessionListActionsHarness, {
      target,
      props: { sessionList },
    });

    target.querySelector<HTMLButtonElement>('[data-testid="new-session"]')?.click();
    await Promise.resolve();
    await Promise.resolve();
    flushSync();

    expect(sessionList.getSnapshot().sessions.map((item) => item.id)).toContain('created');
    expect(sessionList.getSnapshot().selectedSessionId).toBe('created');

    target.querySelector<HTMLButtonElement>('[data-testid="delete-created"]')?.click();
    await Promise.resolve();
    flushSync();

    expect(sessionList.getSnapshot().sessions.map((item) => item.id)).not.toContain('created');
    expect(sessionList.getSnapshot().selectedSessionId).toBe('main');

    await unmount(component);
    target.remove();
  });
});
