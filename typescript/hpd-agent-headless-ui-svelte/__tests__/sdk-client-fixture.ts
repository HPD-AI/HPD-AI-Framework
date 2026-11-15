// Adapts legacy test spies to the current SDK resource shape.
export function installResources<T extends Record<string, any>>(spies: T) {
  const thread = (sessionId: string, threadId: string) => ({
    observe: async ({ agentId, after, signal, onEvent }: any) => {
      const subscription = spies.onAny?.((event: unknown) => onEvent({ event }));
      await spies.start?.({
        agentId,
        sessionId,
        threadId,
        after: { generation: after.generation, sequenceNumber: after.sequence },
        signal,
      });
      let close!: () => void;
      const done = new Promise<void>(resolve => { close = resolve; });
      return {
        done,
        close: async () => {
          subscription?.dispose();
          await spies.stop?.();
          close();
        },
      };
    },
  });

  return Object.assign(spies, {
    sessions: {
      get: async (sessionId: string) => ({
        threads: { get: async (threadId: string) => thread(sessionId, threadId) },
      }),
      search: async (arg: unknown) =>
        (await spies.searchSessions(arg)).map((descriptor: unknown) => ({ descriptor })),
      create: async (arg: unknown) => ({
        session: { descriptor: await spies.createSession(arg) },
      }),
    },
    api: {
      threads: {
        getThread: ({ sessionId, threadId }: any) => spies.getThread(sessionId, threadId),
        getThreadGraph: ({ sessionId }: any) => spies.getThreadGraph(sessionId),
        forkThread: async ({ sessionId, threadId, agentId, input }: any) => {
          const { name, description, tags, newThreadId, ...options } = input;
          return {
            target: await spies.forkThread(sessionId, threadId, {
              ...options,
              agentId,
              ...(name != null ? { name } : {}),
              ...(description != null ? { description } : {}),
              ...(tags != null ? { tags } : {}),
              ...(newThreadId != null ? { newThreadId } : {}),
            }),
          };
        },
      },
      sessions: {
        updateSession: ({ sessionId, input }: any) => spies.updateSession(sessionId, input),
        deleteSession: ({ sessionId }: any) => spies.deleteSession(sessionId),
      },
      agentRuntime: {
        getAgentThreadRuntimeState: ({ agentId, sessionId, threadId }: any) =>
          spies.getThreadState(agentId, sessionId, threadId),
        submitAgentInput: ({ input: { version, ...input }, ...scope }: any) =>
          spies.run({ ...input, ...scope }),
      },
      agentInteractions: {
        respondToAgentRequest: ({
          sessionId,
          threadId,
          input: { version, ...input },
        }: any) => spies.respond({ ...input, sessionId, threadId }),
      },
      executions: {
        listThreadExecutions: ({ agentId, sessionId, threadId }: any) =>
          spies.getThreadExecutions(agentId, sessionId, threadId),
      },
    },
  });
}
