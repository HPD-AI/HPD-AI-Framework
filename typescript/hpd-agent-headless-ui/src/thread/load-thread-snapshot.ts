import type { ThreadExecutionDto } from '@hpd-research/hpd-agent-client';
import type {
  AgentEvent,
  ThreadJournalCursor,
  ThreadSnapshot,
  LoadThreadSnapshotOptions,
  RehydrateOptions,
} from './types.js';

async function loadThreadEvents(
  options: LoadThreadSnapshotOptions,
  observedCursor: ThreadJournalCursor,
): Promise<AgentEvent[]> {
  if (observedCursor.sequenceNumber <= 0) return [];

  const { client, agentId, sessionId, threadId } = options;
  const session = await client.sessions.get(sessionId);
  const thread = await session.threads.get(threadId);
  const events: AgentEvent[] = [];
  let settle!: () => void;
  let fail!: (error: unknown) => void;
  const reachedHead = new Promise<void>((resolve, reject) => {
    settle = resolve;
    fail = reject;
  });

  const accept = (
    event: AgentEvent,
    cursor?: Readonly<{ generation: number; sequence: number }>,
  ) => {
    events.push(event);
    const reached = cursor?.generation === observedCursor.generation
      ? cursor.sequence >= observedCursor.sequenceNumber
      : typeof event.threadSequenceNumber === 'number'
        && event.threadSequenceNumber >= observedCursor.sequenceNumber;
    if (reached) settle();
  };

  const observation = await thread.observe({
    agentId,
    after: {
      generation: observedCursor.generation,
      sequence: 0,
    },
    onEvent: delivery => accept(delivery.event, delivery.cursor),
    onUnknownEvent: delivery => accept(delivery.event as AgentEvent, delivery.cursor),
    onRebase: cursor => fail(new Error(
      `Thread journal generation changed from ${observedCursor.generation} to ${cursor.generation}`,
    )),
  });

  try {
    await Promise.race([
      reachedHead,
      observation.done.then(() => {
        throw new Error('Thread event observation ended before rehydration reached its head');
      }),
    ]);
    return events;
  } finally {
    await observation.close();
  }
}

export async function loadThreadSnapshot(
  options: LoadThreadSnapshotOptions,
  loadOptions: RehydrateOptions = {},
): Promise<ThreadSnapshot> {
  const { client, agentId, sessionId, threadId } = options;

  const threadPromise = client.api.threads.getThread({ sessionId, threadId });
  const statePromise = client.api.agentRuntime.getAgentThreadRuntimeState({ agentId, sessionId, threadId });
  const executionsPromise = loadOptions.includeExecutions
    ? client.api.executions.listThreadExecutions({ agentId, sessionId, threadId })
    : Promise.resolve<ThreadExecutionDto[]>([]);

  const [thread, state, executions] = await Promise.all([
    threadPromise,
    statePromise,
    executionsPromise,
  ]);
  const observedCursor: ThreadJournalCursor = {
    generation: state.observedCursor.generation ?? 1,
    sequenceNumber: state.observedCursor.sequenceNumber ?? 0,
  };
  const events = await loadThreadEvents(options, observedCursor);

  return {
    thread,
    events,
    observedCursor,
    executions,
    activeExecution: state?.activeExecution ?? null,
    pendingRequests: state.pendingRequests.map(({ request, createdAt }) => ({
      request,
      createdAt,
    })),
  };
}
