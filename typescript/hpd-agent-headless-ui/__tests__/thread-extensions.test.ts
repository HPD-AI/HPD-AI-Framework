import { describe, expect, it, vi } from 'vitest';
import {
  createThreadProjection,
  type AgentEvent,
  type ThreadEventExtension,
} from '../src/index.js';

interface AnalysisState {
  latestPercent: number;
  replayCount: number;
}

function analysisExtension(): ThreadEventExtension<AnalysisState> {
  return {
    key: 'acme:analysis-progress@1',
    initialState: { latestPercent: 0, replayCount: 0 },
    matches: event => event.type === 'RUNTIME_AGENT_DURABLE_EVENT'
      || event.type === 'RUNTIME_AGENT_LIVE_EVENT',
    reduce: (state, event, context) => {
      const value = event as Record<string, unknown>;
      if (value.extensionNamespace !== 'acme' || value.schemaName !== 'analysis-progress') {
        return state;
      }
      const payload = value.payload as { percent?: unknown } | undefined;
      return {
        latestPercent: typeof payload?.percent === 'number'
          ? payload.percent
          : state.latestPercent,
        replayCount: state.replayCount + (context.phase === 'rehydrate' ? 1 : 0),
      };
    },
  };
}

function runtimeEvent(type: 'RUNTIME_AGENT_DURABLE_EVENT' | 'RUNTIME_AGENT_LIVE_EVENT'): AgentEvent {
  return {
    type,
    extensionNamespace: 'acme',
    schemaName: 'analysis-progress',
    schemaVersion: 1,
    schemaDigest: 'digest',
    payload: { percent: 40 },
    sessionId: 's1',
    threadId: 'main',
  } as AgentEvent;
}

describe('thread event extensions', () => {
  it('projects known runtime-extension envelopes into application state', () => {
    const projection = createThreadProjection({ extensions: [analysisExtension()] });
    const listener = vi.fn();
    projection.subscribe(listener);
    listener.mockClear();

    projection.project(runtimeEvent('RUNTIME_AGENT_LIVE_EVENT'), {
      phase: 'live',
      route: {
        origin: { sessionId: 's1', threadId: 'main' },
        path: [{ sessionId: 's1', threadId: 'main' }],
      },
      cursor: { generation: 2, sequenceNumber: 11 },
    });

    expect(projection.getSnapshot().extensions).toEqual({
      'acme:analysis-progress@1': {
        latestPercent: 40,
        replayCount: 0,
      },
    });
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('uses the same extension reducer during rehydration and resets on demand', () => {
    const projection = createThreadProjection({ extensions: [analysisExtension()] });

    projection.rehydrate({
      events: [runtimeEvent('RUNTIME_AGENT_DURABLE_EVENT')],
    });

    expect(projection.getSnapshot().extensions).toEqual({
      'acme:analysis-progress@1': {
        latestPercent: 40,
        replayCount: 1,
      },
    });

    projection.reset();

    expect(projection.getSnapshot().extensions).toEqual({
      'acme:analysis-progress@1': {
        latestPercent: 0,
        replayCount: 0,
      },
    });
  });

  it('rejects duplicate extension keys', () => {
    expect(() => createThreadProjection({
      extensions: [analysisExtension(), analysisExtension()],
    })).toThrow("Duplicate thread event extension 'acme:analysis-progress@1'.");
  });
});
