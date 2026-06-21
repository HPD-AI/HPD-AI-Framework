import {
  canEditThreadMessage,
  canRetryThreadMessage,
  createThreadRevisionController,
  type Message,
  type ThreadRevisionController,
  type ThreadRevisionControllerOptions,
  type ThreadRevisionKind,
  type ThreadRevisionOptions,
  type ThreadRevisionResult,
} from '@hpd-research/hpd-agent-headless-ui';
import type {
  ReadableStore,
  StoreSubscriber,
  StoreUnsubscriber,
  ThreadState,
  ThreadStateOptions,
} from './thread-state.js';
import { createThreadState } from './thread-state.js';

export interface ThreadRevisionStateSnapshot {
  running: boolean;
  activeKind: ThreadRevisionKind | null;
  activeClickedMessageId: string | null;
  lastRevision: ThreadRevisionResult | null;
  error: Error | null;
}

export interface ThreadRevisionStateOptions extends ThreadRevisionControllerOptions {
  onRevisionCreated?: (result: ThreadRevisionResult) => void | Promise<void>;
  onError?: (error: Error) => void | Promise<void>;
}

export interface ThreadRevisionState extends ReadableStore<ThreadRevisionStateSnapshot> {
  readonly controller: ThreadRevisionController;
  getSnapshot(): ThreadRevisionStateSnapshot;
  forkAndRetryMessage(messageId: string, options?: ThreadRevisionOptions): Promise<ThreadRevisionResult>;
  forkAndEditMessage(
    messageId: string,
    text: string,
    options?: ThreadRevisionOptions,
  ): Promise<ThreadRevisionResult>;
}

export class ThreadRevisionStateError extends Error {
  constructor(readonly code: 'revision-in-progress', message: string) {
    super(message);
    this.name = 'ThreadRevisionStateError';
  }
}

export type ThreadRevisionHydrationMode = 'none' | 'rehydrate' | 'start';

export interface CreateThreadStateFromRevisionOptions
  extends Omit<ThreadStateOptions, 'threadId'> {
  revision: ThreadRevisionResult | string;
  hydrate?: ThreadRevisionHydrationMode;
  hydrateOptions?: Parameters<ThreadState['start']>[0];
}

export function createThreadRevisionState(options: ThreadRevisionStateOptions): ThreadRevisionState {
  const controller = createThreadRevisionController(options);
  const store = createWritableStore<ThreadRevisionStateSnapshot>({
    running: false,
    activeKind: null,
    activeClickedMessageId: null,
    lastRevision: null,
    error: null,
  });

  const getSnapshot = (): ThreadRevisionStateSnapshot => store.get();

  const runRevision = async (
    kind: ThreadRevisionKind,
    messageId: string,
    operation: () => Promise<ThreadRevisionResult>,
  ): Promise<ThreadRevisionResult> => {
    if (store.get().running) {
      const error = new ThreadRevisionStateError(
        'revision-in-progress',
        'Cannot start a thread revision while another revision is running.',
      );
      await options.onError?.(error);
      throw error;
    }

    store.set({
      ...store.get(),
      running: true,
      activeKind: kind,
      activeClickedMessageId: messageId,
      error: null,
    });

    try {
      const result = await operation();
      store.set({
        running: false,
        activeKind: null,
        activeClickedMessageId: null,
        lastRevision: result,
        error: null,
      });
      await options.onRevisionCreated?.(result);
      return result;
    } catch (caught) {
      const error = normalizeError(caught);
      store.set({
        ...store.get(),
        running: false,
        activeKind: null,
        activeClickedMessageId: null,
        error,
      });
      await options.onError?.(error);
      throw error;
    }
  };

  return {
    controller,
    subscribe: store.subscribe,
    getSnapshot,
    forkAndRetryMessage: (messageId, revisionOptions) =>
      runRevision('retry', messageId, () => controller.forkAndRetryMessage(messageId, revisionOptions)),
    forkAndEditMessage: (messageId, text, revisionOptions) =>
      runRevision('edit', messageId, () => controller.forkAndEditMessage(messageId, text, revisionOptions)),
  };
}

export async function createThreadStateFromRevision(
  options: CreateThreadStateFromRevisionOptions,
): Promise<ThreadState> {
  const {
    revision,
    hydrate = 'start',
    hydrateOptions,
    ...threadOptions
  } = options;
  const threadId = typeof revision === 'string' ? revision : revision.threadId;
  const thread = createThreadState({
    ...threadOptions,
    threadId,
  });

  if (hydrate === 'start') {
    await thread.start(hydrateOptions);
  } else if (hydrate === 'rehydrate') {
    await thread.rehydrate(hydrateOptions);
  }

  return thread;
}

export function canEditMessage(message: Message): boolean {
  return canEditThreadMessage(message);
}

export function canRetryMessage(message: Message): boolean {
  return canRetryThreadMessage(message);
}

function normalizeError(caught: unknown): Error {
  if (caught instanceof Error) return caught;
  return new Error(String(caught));
}

function createWritableStore<T>(
  initialValue: T,
): ReadableStore<T> & { get(): T; set(value: T): void } {
  let value = initialValue;
  const subscribers = new Set<StoreSubscriber<T>>();

  return {
    subscribe(run): StoreUnsubscriber {
      subscribers.add(run);
      run(value);
      return () => {
        subscribers.delete(run);
      };
    },
    get() {
      return value;
    },
    set(nextValue) {
      value = nextValue;
      for (const subscriber of subscribers) {
        subscriber(value);
      }
    },
  };
}
