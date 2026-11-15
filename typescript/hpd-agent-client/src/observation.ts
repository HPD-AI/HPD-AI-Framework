import type { HpdAgentEvent } from './generated/events.js';
import { isRecord, validateEvent } from './event-schema.js';
import type { AgentEventsService, AgentEventsServiceObserveLiveEventsWithSseArgs } from './generated/services.js';
import {
  HpdApiError,
  HpdJournalRebasedError,
  HpdSseState,
  HpdStreamError,
  type HpdSseAppliedCursor,
  type RequestOptions,
} from './runtime.js';

export interface ThreadKey {
  readonly sessionId: string;
  readonly threadId: string;
}

export interface EventRoute {
  readonly origin: ThreadKey;
  readonly path: readonly ThreadKey[];
  readonly threadExecutionId?: string;
}

export interface EventDelivery<Event> {
  readonly event: Event;
  readonly route: EventRoute;
  readonly phase: 'replay' | 'live';
  readonly cursor?: Readonly<HpdSseAppliedCursor>;
}

export type EventHandlers = {
  [Type in HpdAgentEvent['type']]?: (delivery: EventDelivery<Extract<HpdAgentEvent, { type: Type }>>) => void | Promise<void>;
};

export interface ObserveOptions extends RequestOptions {
  after?: Readonly<HpdSseAppliedCursor>;
  hierarchy?: 'exactThread' | 'directChildren' | 'threadAndDirectChildren' | 'descendants' | 'threadAndDescendants';
  handlers?: EventHandlers;
  onEvent?: (delivery: EventDelivery<HpdAgentEvent>) => void | Promise<void>;
  onUnknownEvent?: (delivery: EventDelivery<Record<string, unknown>>) => void | Promise<void>;
  onRebase?: (cursor: Readonly<HpdSseAppliedCursor>) => void | Promise<void>;
}

export interface ThreadObservation {
  readonly appliedCursor: Readonly<HpdSseAppliedCursor>;
  readonly done: Promise<void>;
  close(): Promise<void>;
}

function isThreadKey(value: unknown): value is ThreadKey {
  return isRecord(value) && typeof value.sessionId === 'string' && value.sessionId.length > 0
    && typeof value.threadId === 'string' && value.threadId.length > 0;
}

function sameThread(a: ThreadKey, b: ThreadKey): boolean {
  return a.sessionId === b.sessionId && a.threadId === b.threadId;
}

function decodeDelivery(data: unknown) {
  if (!isRecord(data) || !isRecord(data.event) || !isRecord(data.route)) {
    throw new HpdStreamError('Invalid agent event delivery');
  }
  const { event, route } = data;
  const { origin, path, threadExecutionId } = route;
  if (typeof event.type !== 'string' || event.version !== '1.0' || !isThreadKey(origin)
    || !Array.isArray(path) || !path.every(isThreadKey) || path.length === 0
    || !sameThread(path[path.length - 1]!, origin)
    || (threadExecutionId != null && typeof threadExecutionId !== 'string')) {
    throw new HpdStreamError('Invalid agent event envelope or route');
  }
  return { event, type: event.type, route: { origin, path, threadExecutionId: threadExecutionId ?? undefined } };
}

export async function waitForRetry(milliseconds: number, signal?: AbortSignal): Promise<void> {
  signal?.throwIfAborted();
  await new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', abort);
      resolve();
    }, milliseconds);
    const abort = () => {
      clearTimeout(timer);
      reject(signal?.reason);
    };
    signal?.addEventListener('abort', abort, { once: true });
  });
}

export async function observeThread(
  service: AgentEventsService,
  scope: Pick<AgentEventsServiceObserveLiveEventsWithSseArgs, 'agentId' | 'sessionId' | 'threadId'>,
  initialCursor: Readonly<HpdSseAppliedCursor>,
  options: ObserveOptions,
): Promise<ThreadObservation> {
  if (!Number.isSafeInteger(initialCursor.generation) || initialCursor.generation < 1
    || !Number.isSafeInteger(initialCursor.sequence) || initialCursor.sequence < 0) {
    throw new HpdStreamError('Invalid initial journal cursor');
  }
  const controller = new AbortController();
  const signal = controller.signal;
  const abort = () => controller.abort(options.signal?.reason);
  options.signal?.addEventListener('abort', abort, { once: true });
  if (options.signal?.aborted) abort();

  let applied = { ...initialCursor };
  let connected = false;
  let ready!: () => void;
  let rejectReady!: (error: unknown) => void;
  const started = new Promise<void>((resolve, reject) => {
    ready = resolve;
    rejectReady = reject;
  });

  const done = (async () => {
    let retries = 0;
    while (!signal.aborted) {
      let rebase = false;
      try {
        for await (const frame of service.observeLiveEventsWithSse(
          {
            ...scope,
            after: `${applied.generation}:${applied.sequence}`,
            hierarchy: options.hierarchy ?? 'exactThread',
            onConnect: () => {
              connected = true;
              ready();
            },
          },
          {
            signal,
            timeoutMs: options.timeoutMs,
            headers: options.headers,
          })) {
          signal.throwIfAborted();
          try {
            if (frame.event === 'thread-journal-rebased') {
              const candidate = new HpdSseState(applied);
              try {
                candidate.apply(frame);
              } catch (error) {
                if (!(error instanceof HpdJournalRebasedError) || options.onRebase === undefined) throw error;
                await options.onRebase({ ...candidate.cursor });
                applied = candidate.cursor;
              }
              rebase = true;
              break;
            }

            if (frame.event !== 'agent-event' && frame.event !== 'live-agent-event') continue;
            const { event, type, route } = decodeDelivery(frame.data);
            const candidate = new HpdSseState(applied);
            let cursor: Readonly<HpdSseAppliedCursor> | undefined;
            if (frame.id !== undefined) {
              if (!sameThread(route.origin, scope) || typeof event.threadSequenceNumber !== 'number') {
                throw new HpdStreamError('Invalid committed event attribution');
              }
              if (candidate.apply(frame, event.threadSequenceNumber) === 'duplicate') continue;
              cursor = Object.freeze({ ...candidate.cursor });
            } else if (frame.event === 'agent-event') {
              throw new HpdStreamError('Replay delivery is missing its cursor');
            }
            const phase = frame.event === 'agent-event' ? 'replay' : 'live';
            const valid = validateEvent(type, event);
            if (valid === false) throw new HpdStreamError(`Invalid ${type} event payload`);
            if (valid === true) {
              const delivery = { event, route, phase, cursor } as unknown as EventDelivery<HpdAgentEvent>;
              const handler = Object.hasOwn(options.handlers ?? {}, type)
                ? options.handlers?.[type as keyof EventHandlers] as ((value: EventDelivery<HpdAgentEvent>) => void | Promise<void>)
                : undefined;
              await handler?.(delivery);
              await options.onEvent?.(delivery);
            } else {
              await options.onUnknownEvent?.({ event, route, phase, cursor });
            }
            if (cursor) applied = { ...cursor };
            retries = 0;
          } catch (error) {
            throw new ObservationApplicationError(error);
          }
        }
      } catch (error) {
        if (signal.aborted) break;
        if (error instanceof ObservationApplicationError) throw error.cause;
        if (!connected || error instanceof HpdStreamError || (error instanceof HpdApiError
          && error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429)) throw error;
        if (retries >= 5) throw error;
      }
      if (rebase) {
        retries = 0;
        continue;
      }
      if (retries >= 5) throw new HpdStreamError('SSE reconnect attempts exhausted');
      await waitForRetry(Math.min(250 * 2 ** retries++, 4_000), signal);
    }
  })().catch(error => {
    if (!signal.aborted) throw error;
  }).finally(() => {
    options.signal?.removeEventListener('abort', abort);
  });

  void done.then(
    () => rejectReady(signal.reason ?? new HpdStreamError('SSE closed before connecting')),
    rejectReady,
  );
  await started;
  return {
    get appliedCursor() { return { ...applied }; },
    done,
    async close() {
      controller.abort();
      await done;
    },
  };
}

class ObservationApplicationError extends Error {
  constructor(cause: unknown) {
    super('Could not apply agent event', { cause });
  }
}
