import {
  createThreadBranchNavigator,
  getBranchChoiceLabel,
  hasActivePathChoices,
  hasForkGroups,
  type ActivePathChoice,
  type ThreadBranchNavigationSnapshot,
  type ThreadBranchNavigator,
  type ThreadBranchNavigatorOptions,
} from '@hpd-research/hpd-agent-headless-ui';
import type { Thread, ThreadForkGroup, ThreadGraph, ThreadRuntimeChild } from '@hpd-research/hpd-agent-client';
import type {
  ReadableStore,
  StoreSubscriber,
  StoreUnsubscriber,
} from './thread-state.js';

export type ThreadBranchNavigationSelectionTrigger =
  | 'select-thread'
  | 'select-fork-group-member'
  | 'previous-in-group'
  | 'next-in-group';

export interface ThreadBranchNavigationSelectionDetails {
  trigger: ThreadBranchNavigationSelectionTrigger;
  threadId: string;
  previousThreadId: string;
  groupId?: string;
  snapshot: ThreadBranchNavigationStateSnapshot;
}

export interface ThreadBranchNavigationStateOptions extends ThreadBranchNavigatorOptions {
  onSelected?: (details: ThreadBranchNavigationSelectionDetails) => void | Promise<void>;
}

export interface ThreadBranchNavigationStateSnapshot {
  navigation: ThreadBranchNavigationSnapshot;
  graph: ThreadGraph;
  current: Thread | null;
  threads: Thread[];
  forkGroups: ThreadForkGroup[];
  activePathChoices: ActivePathChoice[];
  activeLabels: string[];
  runtimeChildren: ThreadRuntimeChild[];
  hasForkGroups: boolean;
  hasActivePathChoices: boolean;
  hasRuntimeChildren: boolean;
  loading: boolean;
  error: Error | null;
}

export interface ThreadBranchNavigationState extends ReadableStore<ThreadBranchNavigationStateSnapshot> {
  readonly navigator: ThreadBranchNavigator;
  getSnapshot(): ThreadBranchNavigationStateSnapshot;
  load(threadId?: string): Promise<ThreadBranchNavigationStateSnapshot>;
  refresh(): Promise<ThreadBranchNavigationStateSnapshot>;
  selectThread(threadId: string): Promise<ThreadBranchNavigationStateSnapshot>;
  selectForkGroupMember(groupId: string, threadId: string): Promise<ThreadBranchNavigationStateSnapshot>;
  previousInGroup(groupId: string): Promise<ThreadBranchNavigationStateSnapshot>;
  nextInGroup(groupId: string): Promise<ThreadBranchNavigationStateSnapshot>;
}

export function createThreadBranchNavigationState(
  options: ThreadBranchNavigationStateOptions,
): ThreadBranchNavigationState {
  const navigator = createThreadBranchNavigator(options);
  const store = createWritableStore(createThreadBranchNavigationStateSnapshot(navigator));

  const emit = (patch: Partial<Pick<ThreadBranchNavigationStateSnapshot, 'loading' | 'error'>> = {}) => {
    store.set(createThreadBranchNavigationStateSnapshot(navigator, patch));
  };

  const runNavigation = async (
    operation: () => Promise<ThreadBranchNavigationSnapshot>,
  ): Promise<ThreadBranchNavigationStateSnapshot> => {
    emit({ loading: true, error: null });
    try {
      await operation();
      emit({ loading: false, error: null });
      return store.get();
    } catch (caught) {
      const error = normalizeError(caught);
      emit({ loading: false, error });
      throw error;
    }
  };

  const notifySelected = async (
    trigger: ThreadBranchNavigationSelectionTrigger,
    previousThreadId: string,
    groupId?: string,
  ): Promise<ThreadBranchNavigationStateSnapshot> => {
    const snapshot = store.get();
    if (snapshot.navigation.threadId === previousThreadId) return snapshot;

    await options.onSelected?.({
      trigger,
      threadId: snapshot.navigation.threadId,
      previousThreadId,
      groupId,
      snapshot,
    });
    return snapshot;
  };

  return {
    navigator,
    subscribe: store.subscribe,
    getSnapshot: store.get,
    load: (threadId) => runNavigation(() => navigator.load(threadId)),
    refresh: () => runNavigation(() => navigator.load()),
    selectThread: async (threadId) => {
      const previousThreadId = navigator.threadId;
      await runNavigation(() => navigator.selectThread(threadId));
      return notifySelected('select-thread', previousThreadId);
    },
    selectForkGroupMember: async (groupId, threadId) => {
      const previousThreadId = navigator.threadId;
      await runNavigation(() => navigator.selectForkGroupMember(groupId, threadId));
      return notifySelected('select-fork-group-member', previousThreadId, groupId);
    },
    previousInGroup: async (groupId) => {
      const previousThreadId = navigator.threadId;
      await runNavigation(() => navigator.previousInGroup(groupId));
      return notifySelected('previous-in-group', previousThreadId, groupId);
    },
    nextInGroup: async (groupId) => {
      const previousThreadId = navigator.threadId;
      await runNavigation(() => navigator.nextInGroup(groupId));
      return notifySelected('next-in-group', previousThreadId, groupId);
    },
  };
}

function createThreadBranchNavigationStateSnapshot(
  navigator: ThreadBranchNavigator,
  patch: Partial<Pick<ThreadBranchNavigationStateSnapshot, 'loading' | 'error'>> = {},
): ThreadBranchNavigationStateSnapshot {
  const navigation = navigator.getSnapshot();
  return {
    navigation,
    graph: navigation.graph,
    current: navigation.current,
    threads: navigation.threads,
    forkGroups: navigation.forkGroups,
    activePathChoices: navigation.activePathChoices,
    activeLabels: navigation.activePathChoices.map(getBranchChoiceLabel).filter(Boolean),
    runtimeChildren: navigation.runtimeChildren,
    hasForkGroups: hasForkGroups(navigation),
    hasActivePathChoices: hasActivePathChoices(navigation),
    hasRuntimeChildren: navigation.hasRuntimeChildren,
    loading: patch.loading ?? false,
    error: patch.error ?? null,
  };
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
