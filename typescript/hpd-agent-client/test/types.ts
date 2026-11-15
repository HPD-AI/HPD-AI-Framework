import {
  AgentClient,
  ClientToolRegistry,
  createExpandedToolHarness,
  model,
  type AgentConfig,
  type AgentConfigUpdate,
  type AgentRunConfig,
  type EventHandlers,
  type MiddlewareReference,
  type ProviderAuthentication,
  type RequestOptions,
  type ThreadHistory,
  type ToolHarnessReference,
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

const portableHarness: ToolHarnessReference = "SupportTools";
const richHarness: ToolHarnessReference = {
  name: "SearchTools",
  functions: ["Search", "Read"],
  config: { maxResults: 20 },
};
const portableMiddleware: MiddlewareReference = "LoggingMiddleware";
const richMiddleware: MiddlewareReference = {
  name: "RateLimitMiddleware",
  config: { requestsPerMinute: 60 },
};
void [portableHarness, richHarness, portableMiddleware, richMiddleware];

// @ts-expect-error A rich ToolHarness reference has no arbitrary top-level fields.
const invalidHarness: ToolHarnessReference = { name: "SearchTools", typo: true };
// @ts-expect-error A rich middleware reference has no arbitrary top-level fields.
const invalidMiddleware: MiddlewareReference = { name: "RateLimitMiddleware", typo: true };
void [invalidHarness, invalidMiddleware];

const config = {
  name: "Support Assistant",
  systemInstructions: "Resolve support requests.",
  clients: {
    chat: model("openai", "gpt-5-mini"),
  },
  toolHarnesses: [
    "SupportTools",
    { name: "SearchTools", functions: ["Search", "Read"] },
  ],
} satisfies AgentConfig;
void config;

const invalidHarnessConfig = {
  // @ts-expect-error AgentConfig preserves the rich ToolHarness reference shape.
  toolHarnesses: [{ name: "SearchTools", typo: true }],
} satisfies AgentConfig;
const invalidMiddlewareConfig = {
  // @ts-expect-error AgentConfig preserves the rich middleware reference shape.
  middlewares: [{ name: "RateLimitMiddleware", typo: true }],
} satisfies AgentConfig;
void [invalidHarnessConfig, invalidMiddlewareConfig];

const patch = {
  systemInstructions: "Escalate ambiguous requests.",
  toolHarnesses: [],
  clear: ["validation"],
} satisfies AgentConfigUpdate;
void patch;

const runConfig = {
  clients: { chat: model("anthropic", "claude-sonnet-4.5") },
  systemInstructions: { append: "Use a concise diagnostic style." },
} satisfies AgentRunConfig;
void runConfig;

const authentication: ProviderAuthentication = {
  type: "api-key",
  secretKey: "configured-key",
};
if (authentication.type === "api-key") {
  const secretKey: string = authentication.secretKey;
  void secretKey;
}

// @ts-expect-error API-key authentication requires secretKey.
const invalidAuthentication: ProviderAuthentication = { type: "api-key" };
const invalidNestedAuthentication = {
  clients: {
    chat: {
      provider: {
        key: "openai",
        // @ts-expect-error Nested provider authentication preserves required fields.
        authentication: { type: "api-key" },
      },
    },
  },
} satisfies AgentConfig;
void [invalidAuthentication, invalidNestedAuthentication];
