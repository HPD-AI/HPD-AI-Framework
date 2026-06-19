import {
  createSessionListController,
  type SessionListController,
  type SessionListControllerOptions,
  type SessionListSnapshot,
} from '@hpd-research/hpd-agent-headless-ui';
import type { ReadableStore, StoreSubscriber } from './thread-state.js';

export type SessionListStateOptions = SessionListControllerOptions;

export interface SessionListState extends ReadableStore<SessionListSnapshot> {
  readonly controller: SessionListController;
  getSnapshot(): SessionListSnapshot;
  load: SessionListController['load'];
  refresh: SessionListController['refresh'];
  select: SessionListController['select'];
  create: SessionListController['create'];
  update: SessionListController['update'];
  delete: SessionListController['delete'];
  clearError: SessionListController['clearError'];
}

export function createSessionListState(options: SessionListStateOptions): SessionListState {
  const controller = createSessionListController(options);
  const store = createWritableStore(controller.getSnapshot());

  const emit = (): void => {
    store.set(controller.getSnapshot());
  };

  controller.subscribe((snapshot) => {
    store.set(snapshot);
  });

  const withStateUpdate = async <T>(operation: () => Promise<T>): Promise<T> => {
    emit();
    try {
      return await operation();
    } finally {
      emit();
    }
  };

  return {
    controller,
    subscribe: store.subscribe,
    getSnapshot: () => controller.getSnapshot(),
    load: (options) => withStateUpdate(() => controller.load(options)),
    refresh: (options) => withStateUpdate(() => controller.refresh(options)),
    select: (sessionId) => {
      const snapshot = controller.select(sessionId);
      emit();
      return snapshot;
    },
    create: (options) => withStateUpdate(() => controller.create(options)),
    update: (sessionId, request, options) => withStateUpdate(() =>
      controller.update(sessionId, request, options)),
    delete: (sessionId, options) => withStateUpdate(() => controller.delete(sessionId, options)),
    clearError: () => {
      controller.clearError();
      emit();
    },
  };
}

function createWritableStore<T>(initialValue: T): ReadableStore<T> & { set(value: T): void } {
  let value = initialValue;
  const subscribers = new Set<StoreSubscriber<T>>();

  return {
    subscribe(run) {
      subscribers.add(run);
      run(value);
      return () => {
        subscribers.delete(run);
      };
    },
    set(nextValue) {
      value = nextValue;
      for (const subscriber of subscribers) {
        subscriber(value);
      }
    },
  };
}
