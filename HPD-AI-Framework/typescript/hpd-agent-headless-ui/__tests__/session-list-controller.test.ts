import { describe, expect, it, vi } from 'vitest';
import type { AgentClient, Session } from '@hpd-research/hpd-agent-client';
import {
  createSessionListController,
  getSessionLabel,
  readSessionMetadataString,
} from '../src/index.js';

const stamp = '2026-01-01T00:00:00.000Z';

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
      const created = session(request.sessionId ?? `s${current.length + 1}`, request.metadata ?? {});
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

describe('createSessionListController', () => {
  it('loads metadata-filtered sessions and auto-selects the first match', async () => {
    const client = fakeClient([
      session('one', { 'hpdos.workspaceKey': 'alpha', name: 'Alpha' }),
      session('two', { 'hpdos.workspaceKey': 'beta', name: 'Beta' }),
    ]);
    const controller = createSessionListController({
      client,
      search: { metadata: { 'hpdos.workspaceKey': 'alpha' } },
    });

    const snapshot = await controller.load();

    expect(snapshot.sessions.map((item) => item.id)).toEqual(['one']);
    expect(snapshot.selectedSessionId).toBe('one');
    expect(snapshot.items[0]).toMatchObject({
      id: 'one',
      label: 'Alpha',
      selected: true,
    });
  });

  it('creates, selects, updates, and deletes sessions', async () => {
    const client = fakeClient([session('main', { name: 'Main' })]);
    const controller = createSessionListController({ client });
    await controller.load();

    const created = await controller.create({
      sessionId: 'new',
      metadata: { name: 'New', workspace: 'demo' },
    });
    expect(created.id).toBe('new');
    expect(controller.getSnapshot().selectedSessionId).toBe('new');

    await controller.update('new', { metadata: { name: 'Renamed', workspace: null } });
    expect(controller.getSnapshot().items[0].label).toBe('Renamed');
    expect(controller.getSnapshot().sessions[0].metadata.workspace).toBeUndefined();

    await controller.delete('new');
    expect(controller.getSnapshot().selectedSessionId).toBe('main');
  });

  it('uses metadata helpers without baking app concepts into the controller', () => {
    const item = session('abcdef0123456789', {
      'hpdos.name': 'Workspace session',
      'hpdos.workspaceKey': 'workspace-a',
    });

    expect(getSessionLabel(item)).toBe('Workspace session');
    expect(readSessionMetadataString(item, 'hpdos.workspaceKey')).toBe('workspace-a');
  });
});
