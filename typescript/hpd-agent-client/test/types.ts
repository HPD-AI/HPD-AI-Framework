import {
  AgentClient,
  ClientToolRegistry,
  createExpandedToolHarness,
  type EventHandlers,
} from '../src/index.js';

declare const client: AgentClient;
const { thread } = await client.sessions.create({ agentId: 'assistant' });

const handlers: EventHandlers = {
  TEXT_DELTA: ({ event }) => {
    const text: string = event.text;
    // @ts-expect-error Text deltas contain string text.
    const invalid: number = text;
  },
};

await thread.observe({ handlers });

const state = await thread.state();
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
});

const harness = createExpandedToolHarness('test', 'Test tools', [{
  name: 'echo',
  description: 'Echo a value.',
  parametersSchema: { type: 'object' },
}]);
new ClientToolRegistry().registerHarness(harness, () => 'echoed');
