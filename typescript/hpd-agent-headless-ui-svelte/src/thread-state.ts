import {
  canSubmitText,
  createThreadController,
  getActiveToolCalls,
  getPendingRuntimeRequests,
  getTextSubmissionState,
  getThreadContextUsage,
  getThreadTimeline,
  getThreadWorkGroups,
  getTranscriptMessages,
  type Message,
  type RuntimeRequest,
  type TextSubmissionState,
  type ThreadActivity,
  type ThreadContextUsage,
  type ThreadController,
  type ThreadControllerOptions,
  type ThreadProjectionSnapshot,
  type ThreadTimelineItem,
  type ThreadTimelineOptions,
  type ThreadWorkGroup,
  type ToolCall,
} from '@hpd-research/hpd-agent-headless-ui';

export type StoreUnsubscriber = () => void;
export type StoreSubscriber<T> = (value: T) => void;

export interface ReadableStore<T> {
  subscribe(run: StoreSubscriber<T>): StoreUnsubscriber;
}

export interface ThreadStateOptions extends ThreadControllerOptions {
  timelineOptions?: ThreadTimelineOptions;
}

export interface ThreadStateSnapshot {
  projection: ThreadProjectionSnapshot;
  timeline: ThreadTimelineItem[];
  workGroups: ThreadWorkGroup[];
  transcriptMessages: Message[];
  activity: ThreadActivity;
  activeTools: ToolCall[];
  pendingRuntimeRequests: RuntimeRequest[];
  contextUsage: ThreadContextUsage | null;
  textSubmissionState: TextSubmissionState;
  canSubmitText: boolean;
  loading: boolean;
  connected: boolean;
  error: string | null;
}

export interface ThreadState extends ReadableStore<ThreadStateSnapshot> {
  readonly controller: ThreadController;
  getSnapshot(): ThreadStateSnapshot;
  clearError: ThreadController['clearError'];
  start: ThreadController['start'];
  rehydrate: ThreadController['rehydrate'];
  connect: ThreadController['connect'];
  disconnect: ThreadController['disconnect'];
  dispose: ThreadController['dispose'];
  sendMessage: ThreadController['sendMessage'];
  run: ThreadController['run'];
  respond: ThreadController['respond'];
  interrupt: ThreadController['interrupt'];
  approve: ThreadController['approve'];
  deny: ThreadController['deny'];
  clarify: ThreadController['clarify'];
  answerClientToolRequest: ThreadController['answerClientToolRequest'];
}

export function createThreadState(options: ThreadStateOptions): ThreadState {
  const controller = createThreadController(options);
  const store = createWritableStore(createThreadStateSnapshot(controller, options.timelineOptions));
  let disposed = false;

  const emit = (): void => {
    store.set(createThreadStateSnapshot(controller, options.timelineOptions));
  };

  const unsubscribeProjection = controller.projection.subscribe(emit);

  const withStateUpdate = async <T>(operation: () => Promise<T>): Promise<T> => {
    emit();
    try {
      return await operation();
    } finally {
      emit();
    }
  };

  const state: ThreadState = {
    controller,
    subscribe: store.subscribe,
    getSnapshot: () => createThreadStateSnapshot(controller, options.timelineOptions),
    clearError: () => {
      controller.clearError();
      emit();
    },
    start: (options) => withStateUpdate(() => controller.start(options)),
    rehydrate: (options) => withStateUpdate(() => controller.rehydrate(options)),
    connect: (options) => withStateUpdate(() => controller.connect(options)),
    disconnect: () => withStateUpdate(() => controller.disconnect()),
    dispose: async () => {
      if (disposed) return;
      disposed = true;
      unsubscribeProjection();
      await withStateUpdate(() => controller.dispose());
    },
    sendMessage: (input, options) => withStateUpdate(() => controller.sendMessage(input, options)),
    run: (input) => withStateUpdate(() => controller.run(input)),
    respond: (input) => withStateUpdate(() => controller.respond(input)),
    interrupt: (options) => withStateUpdate(() => controller.interrupt(options)),
    approve: (permissionId, choice) => withStateUpdate(() => controller.approve(permissionId, choice)),
    deny: (permissionId, reason) => withStateUpdate(() => controller.deny(permissionId, reason)),
    clarify: (requestId, answer) => withStateUpdate(() => controller.clarify(requestId, answer)),
    answerClientToolRequest: (requestId, outcome, options) =>
      withStateUpdate(() => controller.answerClientToolRequest(requestId, outcome, options)),
  };

  return state;
}

function createThreadStateSnapshot(
  controller: ThreadController,
  timelineOptions?: ThreadTimelineOptions,
): ThreadStateSnapshot {
  const projection = controller.projection.getSnapshot();
  return {
    projection,
    timeline: getThreadTimeline(projection, timelineOptions),
    workGroups: getThreadWorkGroups(projection, timelineOptions),
    transcriptMessages: getTranscriptMessages(projection),
    activity: projection.activity,
    activeTools: getActiveToolCalls(projection),
    pendingRuntimeRequests: getPendingRuntimeRequests(projection),
    contextUsage: getThreadContextUsage(projection),
    textSubmissionState: getTextSubmissionState(projection),
    canSubmitText: canSubmitText(projection),
    loading: controller.loading,
    connected: controller.connected,
    error: projection.error ?? controller.error,
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
