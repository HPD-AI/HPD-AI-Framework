import {
  AgentClient,
  ClientToolRegistry,
  createExpandedToolHarness,
  type EventHandlers,
  type RequestOptions,
  type ThreadHistory,
} from '../src/index.js';

declare const client: AgentClient;
declare const signal: AbortSignal;
const requestOptions: RequestOptions = { signal, timeoutMs: 10_000 };
const { session, thread } = await client.sessions.create(
  { agentId: 'assistant' },
  requestOptions);

const handlers: EventHandlers = {
  TEXT_DELTA: ({ event }) => {
    const text: string = event.text;
    // @ts-expect-error Text deltas contain string text.
    const invalid: number = text;
  },
};

await thread.observe({ handlers, ...requestOptions });

const state = await thread.state({ ...requestOptions, agentId: 'assistant' });
const pendingRequest = state.pendingRequests[0]?.request;
if (pendingRequest) {
  const type: 'CLIENT_TOOL_INVOKE_REQUEST'
    | 'CONTINUATION_REQUEST'
    | 'PARENT_QUESTION_REQUEST'
    | 'PERMISSION_REQUEST'
    | 'USER_QUESTION_REQUEST' = pendingRequest.type;
}

// @ts-expect-error A permission response must select a declared choice.
await thread.respond({
  type: 'PERMISSION_RESPONSE',
  permissionId: 'permission-1',
  sourceName: 'test',
});

await thread.respond({
  type: 'PERMISSION_RESPONSE',
  permissionId: 'permission-1',
  sourceName: 'test',
  choiceId: 'allow_once',
}, requestOptions);

const history: ThreadHistory = await thread.history(requestOptions);
const firstText: string | undefined = history.messages[0]?.text;
const appliedGeneration: number = history.appliedCursor.generation;

const content = await thread.content.upload(new Blob(['hello']), 'hello.txt', requestOptions);
const contentRef = content.toMessageContent('hello.txt');
await thread.content.list(requestOptions);
await thread.content.download(content.contentId, requestOptions);
await thread.content.delete(content.contentId, requestOptions);
await thread.executions.list({ ...requestOptions, agentId: 'assistant' });
await thread.contextUsage({ ...requestOptions, agentId: 'assistant' });
await thread.graph(requestOptions);
await thread.subagents(requestOptions);
await thread.update({ name: 'Renamed', description: null, tags: null }, requestOptions);
await session.update({ metadata: {} }, requestOptions);

await client.agents.list(requestOptions);
await client.agents.get('assistant', requestOptions);
await client.evaluations.catalog(requestOptions);
await client.api.agents.listAgents({}, requestOptions);

const harness = createExpandedToolHarness('test', 'Test tools', [{
  name: 'echo',
  description: 'Echo a value.',
  parametersSchema: { type: 'object' },
}]);
new ClientToolRegistry().registerHarness(harness, () => 'echoed');
