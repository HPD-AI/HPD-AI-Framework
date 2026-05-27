# HPD-Agent Infrastructure Reference

This file records the HPD-Agent infrastructure that HPD-OS chat should build on. It is intentionally frontend-facing: the goal is not to mirror every backend type, but to preserve the contracts and design constraints that matter for the chat, coding, and automation surfaces.

## Mount Point

HPD-OS mounts the HPD-Agent API at:

```text
/api/hpd-agent
```

The backend runtime discovery endpoint also exposes this as the agent API root.

## Endpoint Contracts

All endpoint paths below are relative to:

```text
/api/hpd-agent
```

### Agent Endpoints

```text
GET    /agents
POST   /agents
GET    /agents/{agentId}
PUT    /agents/{agentId}
DELETE /agents/{agentId}
```

`GET /agents`

- Request body: none.
- Returns: `AgentSummaryDto[]`.

`AgentSummaryDto`:

```ts
type AgentSummaryDto = {
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
  metadata?: Record<string, unknown> | null;
};
```

`POST /agents`

- Request body: `CreateAgentRequest`.
- Returns: `201 Created` with `StoredAgentDto`.

```ts
type CreateAgentRequest = {
  name: string;
  config: AgentConfig;
  metadata?: Record<string, unknown> | null;
};
```

`GET /agents/{agentId}`

- Request body: none.
- Returns: `StoredAgentDto`, `404`, or validation problem.

`PUT /agents/{agentId}`

- Request body: `UpdateAgentRequest`.
- Returns: updated `StoredAgentDto`, `404`, or validation problem.

```ts
type UpdateAgentRequest = {
  config: AgentConfig;
};
```

`DELETE /agents/{agentId}`

- Request body: none.
- Returns: `204`, `404`, or validation problem.

`StoredAgentDto`:

```ts
type StoredAgentDto = {
  id: string;
  name: string;
  config: AgentConfig;
  createdAt: string;
  updatedAt: string;
  metadata?: Record<string, unknown> | null;
};
```

`AgentConfig` is broad. Frontend code should pass through unknown config fields instead of trying to own the full schema. Important known fields include:

- `name`;
- `systemInstructions`;
- `maxAgenticIterations`;
- `continuationExtensionAmount`;
- `provider`;
- `validation`;
- `mcp`;
- `errorHandling`;
- `documentHandling`;
- `historyReduction`;
- `agenticLoop`;
- `messages`;
- `toolSelection`;
- `collapsing`;
- `harnesses`;
- `middlewares`;
- `caching`;
- `observability`;
- `backgroundResponses`;
- `audio`;
- `includeReasoningInModelHistory`;
- `defaultReasoning`;
- `coalesceDeltas`.

### Session Endpoints

```text
POST   /sessions
GET    /sessions
POST   /sessions/search
GET    /sessions/{sessionId}
PATCH  /sessions/{sessionId}
DELETE /sessions/{sessionId}
```

`POST /sessions`

- Request body: optional `CreateSessionRequest`.
- Returns: `201 Created` with `SessionDto`.

```ts
type CreateSessionRequest = {
  sessionId?: string | null;
  metadata?: Record<string, unknown> | null;
};
```

`GET /sessions`

- Request body: none.
- Returns: `SessionDto[]`.

`POST /sessions/search`

- Request body: optional `SearchSessionsRequest`.
- Returns: `SessionDto[]`.

```ts
type SearchSessionsRequest = {
  metadata?: Record<string, unknown> | null;
  offset?: number;
  limit?: number;
};
```

`GET /sessions/{sessionId}`

- Request body: none.
- Returns: `SessionDto`, `404`, or validation problem.

`PATCH /sessions/{sessionId}`

- Request body: `UpdateSessionRequest`.
- Returns: updated `SessionDto`, `404`, or validation problem.
- Merge semantics: provided keys are added or overwritten; keys set to `null` are removed.

```ts
type UpdateSessionRequest = {
  metadata: Record<string, unknown | null>;
};
```

`DELETE /sessions/{sessionId}`

- Request body: none.
- Deletes session, branches, and assets.
- Returns: `204`, `404`, or validation problem.

`SessionDto`:

```ts
type SessionDto = {
  id: string;
  createdAt: string;
  lastActivity: string;
  metadata?: Record<string, unknown> | null;
};
```

### Branch Endpoints

```text
GET    /sessions/{sid}/branches
GET    /sessions/{sid}/branches/{bid}
POST   /agents/{agentId}/sessions/{sid}/branches
POST   /agents/{agentId}/sessions/{sid}/branches/{bid}/fork
PATCH  /sessions/{sid}/branches/{bid}
DELETE /sessions/{sid}/branches/{bid}?recursive=false
GET    /sessions/{sid}/branches/{bid}/events
GET    /sessions/{sid}/branches/{bid}/siblings
```

`GET /sessions/{sid}/branches`

- Request body: none.
- Returns: `BranchDto[]`, `404`, or validation problem.

`GET /sessions/{sid}/branches/{bid}`

- Request body: none.
- Returns: `BranchDto`, `404`, or validation problem.

`POST /agents/{agentId}/sessions/{sid}/branches`

- Request body: `CreateBranchRequest`.
- Returns: `201 Created` with `BranchDto`, `404`, `409`, or validation problem.

```ts
type CreateBranchRequest = {
  branchId: string;
  name?: string | null;
  description?: string | null;
  tags?: string[] | null;
};
```

`POST /agents/{agentId}/sessions/{sid}/branches/{bid}/fork`

- Request body: `ForkBranchRequest`.
- Returns: `201 Created` with `BranchDto`, `404`, or validation problem.

```ts
type ForkBranchRequest = {
  newBranchId?: string | null;
  fromMessageIndex: number;
  name?: string | null;
  description?: string | null;
  tags?: string[] | null;
};
```

`PATCH /sessions/{sid}/branches/{bid}`

- Request body: `UpdateBranchRequest`.
- Returns: updated `BranchDto`, `404`, or validation problem.
- Only non-null fields are applied.

```ts
type UpdateBranchRequest = {
  name?: string | null;
  description?: string | null;
  tags?: string[] | null;
};
```

`DELETE /sessions/{sid}/branches/{bid}?recursive=false`

- Request body: none.
- Query: `recursive`, default `false`.
- Returns: `204`, `404`, `409`, or validation problem.

`GET /sessions/{sid}/branches/{bid}/events`

- Request body: none.
- Returns: `BranchEvent[]`, `404`, or validation problem.
- The returned events are durable branch events serialized as `AgentEvent`-compatible objects with branch metadata.

`GET /sessions/{sid}/branches/{bid}/siblings`

- Request body: none.
- Returns: `BranchDto[]`, `404`, or validation problem.

`BranchDto`:

```ts
type BranchDto = {
  id: string;
  sessionId: string;
  name: string;
  description?: string | null;
  forkedFrom?: string | null;
  forkedAtMessageIndex?: number | null;
  createdAt: string;
  lastActivity: string;
  messageCount: number;
  tags?: string[] | null;
  ancestors?: Record<string, string> | null;
  siblingIndex: number;
  totalSiblings: number;
  isOriginal: boolean;
  originalBranchId?: string | null;
  previousSiblingId?: string | null;
  nextSiblingId?: string | null;
  totalForks: number;
};
```

`BranchEvent`:

```ts
type BranchEvent = AgentEvent & {
  eventId?: string;
  sessionId?: string;
  branchId?: string;
  branchEventCategory?: "Domain" | "Runtime";
  sequenceNumber?: number;
  timestamp?: string;
  eventFlowId?: string;
};
```

Branch event `type` values intentionally reuse the live `AgentEvent` names for transcript activity, for example `TEXT_DELTA`, `REASONING_DELTA`, `TOOL_CALL_RESULT`, and `MESSAGE_TURN_FINISHED`. Branch-only events use branch-specific event classes such as `BRANCH_CREATED`, `MESSAGE_STARTED`, `MESSAGE_COMPLETED`, and `BRANCH_MIDDLEWARE_STATE_COMMITTED`.

### AI Content Model

Durable branch events can carry `Microsoft.Extensions.AI` content through events such as `CONTENT_ADDED`, `TOOL_CALL_RESULT`, and projected message reconstruction. The frontend should keep the content model full-fidelity and avoid flattening it too early.

Useful `ChatMessage` shape when sending richer input or reconstructing a message projection:

```ts
type ChatMessage = {
  role: "system" | "assistant" | "user" | "tool" | string;
  contents: AIContent[];
  authorName?: string | null;
  createdAt?: string | null;
  messageId?: string | null;
  additionalProperties?: Record<string, unknown> | null;
};
```

`ChatMessage.text` in .NET is only a convenience concatenation of `TextContent` items. The frontend should not rely on a flattened `text` field when rendering hydrated history. Render from `contents`.

`AIContent` is polymorphic using the `$type` discriminator.

Built-in Microsoft.Extensions.AI content discriminators include:

- `$type: "text"`: normal text content.
- `$type: "reasoning"`: model reasoning/thinking content, distinct from normal output text.
- `$type: "functionCall"`: function/tool call request, with `callId`, `name`, and `arguments`.
- `$type: "functionResult"`: function/tool result, with `callId` and `result`.
- `$type: "toolCall"`: generic tool call base content.
- `$type: "toolResult"`: generic tool result base content.
- `$type: "data"`: binary data content.
- `$type: "uri"`: URI-backed content.
- `$type: "hostedFile"`: provider-hosted file content.
- `$type: "hostedVectorStore"`: provider-hosted vector store content.
- `$type: "error"`: error content.
- `$type: "usage"`: usage/billing metadata content.
- `$type: "inputRequest"`: generic input request content.
- `$type: "inputResponse"`: generic input response content.
- `$type: "toolApprovalRequest"`: tool approval request content.
- `$type: "toolApprovalResponse"`: tool approval response content.
- `$type: "mcpServerToolCall"` / `$type: "mcpServerToolResult"`.
- `$type: "imageGenerationToolCall"` / `$type: "imageGenerationToolResult"`.
- `$type: "codeInterpreterToolCall"` / `$type: "codeInterpreterToolResult"`.
- `$type: "webSearchToolCall"` / `$type: "webSearchToolResult"`.

HPD-Agent extends this content model during session serialization with:

- `$type: "hpd:image"`;
- `$type: "hpd:audio"`;
- `$type: "hpd:video"`;
- `$type: "hpd:document"`.

Content can also carry:

- `annotations`;
- `additionalProperties`;
- content-specific fields.

Some .NET-only/debug fields are not serialized, including raw provider representations and exceptions on function call/result content. The frontend should not expect those fields during hydration.

Rendering rules:

- Preserve unknown `$type` content and show it in a fallback inspector.
- Render `text` as normal visible message text.
- Render `reasoning` separately from assistant output text.
- Render `functionCall` and `functionResult` as tool/coding blocks, not prose.
- Use `callId` to pair function calls and results.
- Treat `functionResult.result` as structured data: it may be a string, JSON object, primitive, `ToolResultPayload`, content, or provider-specific JSON.
- Do not discard `additionalProperties`; providers and middleware may use it for useful metadata.

### Asset Endpoints

```text
POST   /sessions/{sid}/assets
GET    /sessions/{sid}/assets
GET    /sessions/{sid}/assets/{assetId}
DELETE /sessions/{sid}/assets/{assetId}
```

`POST /sessions/{sid}/assets`

- Request body: `multipart/form-data`.
- Required form field: `file`.
- Returns: `201 Created` with `AssetDto`, `404`, or validation problem.

`GET /sessions/{sid}/assets`

- Request body: none.
- Returns: `AssetDto[]`, `404`, or validation problem.

`GET /sessions/{sid}/assets/{assetId}`

- Request body: none.
- Returns: binary file content, `404`, or validation problem.

`DELETE /sessions/{sid}/assets/{assetId}`

- Request body: none.
- Returns: `204`, `404`, or validation problem.

`AssetDto`:

```ts
type AssetDto = {
  assetId: string;
  contentType: string;
  sizeBytes: number;
  createdAt: string;
};
```

### Streaming Endpoints

```text
POST /agents/{agentId}/sessions/{sid}/branches/{bid}/stream
POST /agents/{agentId}/sessions/{sid}/branches/{bid}/events/stream
GET  /agents/{agentId}/sessions/{sid}/branches/{bid}/ws
```

`POST /agents/{agentId}/sessions/{sid}/branches/{bid}/stream`

- Request body: `StreamTextRequest`.
- `text` must not be blank.
- Returns: SSE stream of serialized agent events.
- Can return `400`, `404`, or `409` before streaming starts.

```ts
type StreamTextRequest = {
  text: string;
  runConfig?: AgentRunConfig | null;
};
```

The backend wraps this as:

```ts
type UserTextInputEvent = {
  version?: string;
  type: "USER_TEXT_INPUT";
  text: string;
  runConfig?: AgentRunConfig | null;
};
```

Route scope supplies `agentId`, `sessionId`, and `branchId`.

`POST /agents/{agentId}/sessions/{sid}/branches/{bid}/events/stream`

- Request body: serialized `AgentInputEvent` envelope.
- Returns: SSE stream of serialized agent events.
- If the body cannot parse as `AgentInputEvent`, returns `400`.
- Can return `404` or `409` before streaming starts.

Useful raw input envelopes:

```ts
type RawUserTextInputEvent = {
  version?: string;
  type: "USER_TEXT_INPUT";
  text: string;
  runConfig?: AgentRunConfig | null;
};

type RawUserMessagesInputEvent = {
  version?: string;
  type: "USER_MESSAGES_INPUT";
  messages: unknown[];
  runConfig?: AgentRunConfig | null;
};

type RawInterruptionRequestEvent = {
  version?: string;
  type: "INTERRUPTION_REQUEST";
  eventFlowId?: string | null;
  reason: string;
  source: "User" | "System" | "Parent" | "Middleware";
};
```

`GET /agents/{agentId}/sessions/{sid}/branches/{bid}/ws`

- Must be a WebSocket request.
- Client sends text frames containing serialized agent input events or bidirectional response events.
- Server sends text frames containing serialized agent output events.
- Invalid payload closes with `InvalidPayloadData`.
- Can return `404` or `409` before WebSocket acceptance.

The WebSocket path accepts both:

- `AgentInputEvent`;
- `IBidirectionalEvent` response events.

### Middleware Response Endpoints

```text
POST /agents/{agentId}/sessions/{sid}/branches/{bid}/permissions/respond
POST /agents/{agentId}/sessions/{sid}/branches/{bid}/continuation/respond
POST /agents/{agentId}/sessions/{sid}/branches/{bid}/clarifications/respond
POST /agents/{agentId}/sessions/{sid}/branches/{bid}/client-tools/respond
```

These endpoints return `200`, `404`, `409`, or validation problem.

`POST /permissions/respond`

- Request body: `PermissionResponseEvent`.
- `permissionId` must match the pending `PermissionRequestEvent.permissionId`.

```ts
type PermissionResponseEvent = {
  version?: string;
  type?: "PERMISSION_RESPONSE";
  permissionId: string;
  sourceName: string;
  approved: boolean;
  reason?: string | null;
  choice?: "Ask" | "AlwaysAllow" | "AlwaysDeny";
};
```

Corresponding request event:

```ts
type PermissionRequestEvent = {
  type: "PERMISSION_REQUEST";
  permissionId: string;
  sourceName: string;
  functionName: string;
  description?: string | null;
  callId: string;
  arguments?: Record<string, unknown> | null;
};
```

`POST /continuation/respond`

- Request body: `ContinuationResponseEvent`.
- `continuationId` must match the pending `ContinuationRequestEvent.continuationId`.

```ts
type ContinuationResponseEvent = {
  version?: string;
  type?: "CONTINUATION_RESPONSE";
  continuationId: string;
  sourceName: string;
  approved: boolean;
  extensionAmount?: number;
};
```

Corresponding request event:

```ts
type ContinuationRequestEvent = {
  type: "CONTINUATION_REQUEST";
  continuationId: string;
  sourceName: string;
  currentIteration: number;
  maxIterations: number;
};
```

`POST /clarifications/respond`

- Request body: `ClarificationResponseEvent`.
- `requestId` must match the pending `ClarificationRequestEvent.requestId`.

```ts
type ClarificationResponseEvent = {
  version?: string;
  type?: "CLARIFICATION_RESPONSE";
  requestId: string;
  sourceName: string;
  question: string;
  answer: string;
};
```

Corresponding request event:

```ts
type ClarificationRequestEvent = {
  type: "CLARIFICATION_REQUEST";
  requestId: string;
  sourceName: string;
  question: string;
  agentName?: string | null;
  options?: string[] | null;
};
```

`POST /client-tools/respond`

- Request body: `ClientToolInvokeResponseEvent`.
- `requestId` must match the pending `ClientToolInvokeRequestEvent.requestId`.

```ts
type ClientToolInvokeResponseEvent = {
  version?: string;
  type?: "CLIENT_TOOL_INVOKE_RESPONSE";
  requestId: string;
  content: unknown[];
  success?: boolean;
  errorMessage?: string | null;
  augmentation?: unknown | null;
};
```

Corresponding request event:

```ts
type ClientToolInvokeRequestEvent = {
  type: "CLIENT_TOOL_INVOKE_REQUEST";
  requestId: string;
  toolName: string;
  callId: string;
  arguments: Record<string, unknown>;
  description?: string | null;
};
```

### Common HTTP Results

The API commonly uses:

- `200 OK` for successful reads, updates, and response submissions;
- `201 Created` for created agents, sessions, branches, forks, and assets;
- `204 No Content` for successful deletes;
- `400 Bad Request` for invalid stream input;
- `404 Not Found` when agent/session/branch/asset scope does not exist;
- `409 Conflict` when a branch/stream operation conflicts with current state;
- validation problem JSON for domain validation failures.

## Core Model

HPD-Agent is not a simple prompt-to-text endpoint. It is a full agent runtime with:

- agent definitions;
- sessions;
- branchable conversations;
- session-scoped assets;
- streaming and bidirectional transport;
- typed user input events;
- per-run configuration;
- typed output events;
- human-in-the-loop workflows;
- coding harness telemetry.

The frontend should treat HPD-Agent as an evented runtime. Chat messages are a projection of the event stream, not the source of truth.

## Agent Definitions

Agents are first-class runtime definitions.

Supported operations:

- list agents;
- create an agent;
- read one agent;
- update an agent;
- delete an agent.

This means HPD-OS can support multiple agent workflows without hardcoding one chat bot into the UI.

## Sessions

Sessions are provider-agnostic containers for agent work.

Supported operations:

- create session;
- list sessions;
- search sessions;
- read session;
- patch session metadata;
- delete session.

Session-scoped state can be shared across branches. Examples include assets, permissions, and user/session preferences.

## Branches

Branches are first-class conversation paths inside a session.

Supported operations:

- list branches for a session;
- read a branch;
- create a branch;
- fork a branch;
- update a branch;
- delete a branch;
- load branch events;
- inspect sibling branches.

This enables "try another direction from here" without corrupting the main conversation.

## Branch Event Persistence And Hydration

Branch hydration is now event-sourced. `branch.json` persists a `BranchEventDocument`, not a direct serialized `Branch` object.

The document stores ordered `AgentEvent` instances with branch scope metadata:

```ts
type BranchEventDocument = {
  schema: "hpd.agent.branch.events";
  version: 2;
  sessionId: string;
  branchId: string;
  createdAt: string;
  updatedAt: string;
  nextSequenceNumber: number;
  events: BranchEvent[];
};
```

The same event protocol is used for live stream events and durable branch history:

```text
live run:
  AgentEvent[] from SSE/WebSocket

hydration:
  BranchEvent[] from GET /sessions/{sid}/branches/{bid}/events
  where BranchEvent = AgentEvent + branch metadata
```

The branch projector reconstructs `Branch.Messages` from the durable event log for backend compatibility, but HPD-OS should hydrate chat UI from `getBranchEvents()`.

Durable branch events include:

- branch metadata and fork/tree events;
- message boundaries;
- coalesced text content;
- coalesced reasoning content;
- tool call, argument, completion, and result events;
- committed middleware state;
- selected runtime lifecycle events such as turn start, finish, and failure.

The important optimization is that durable branch events are semantic, not raw token replay. Normal text and reasoning are coalesced when committed, so `branch.json` does not store every streaming token/chunk.

What branch hydration does not automatically give back is every transient runtime event exactly as it arrived:

- original `TEXT_DELTA` chunk boundaries and timing;
- original `REASONING_DELTA` chunk boundaries and timing;
- live-only `EXECUTE_COMMAND_OUTPUT_CHUNK` events;
- intermediate command progress events;
- command background-list inspection events;
- transient lifecycle events;
- transient language-server events unless explicitly persisted;
- transient permission/clarification lifecycle unless explicitly persisted or represented in committed events;
- raw diagnostic events.

Coding harness command cards are recoverable through durable command lifecycle events and tool result events. `EXECUTE_COMMAND_PROCESS_STARTED` and `EXECUTE_COMMAND_PROCESS_EXITED` provide command identity, cwd, shell, status, duration, truncation, and artifact/content references. `TOOL_CALL_RESULT` provides the durable final visible output fallback. Live command-output chunks are intentionally not durable; they exist for streaming feel during an active run.

The UI should therefore support one reducer that accepts both event sources:

```text
startup/reload:
  getBranchEvents() -> apply durable BranchEvent[] -> transcript/tool/coding projection

active run:
  live AgentEvent stream -> apply events to the same projection
```

Do not assume hydration can replay exact typing animations or process-output timing. It reconstructs the durable semantic conversation state.

### Physical Session Store Layout

HPD-OS configures HPD-Agent with a JSON session store.

In development, the default root is:

```text
HPDOS/backend/.hpdos/sessions
```

This can be overridden with:

```text
HPDOS:DataRoot
HPDOS:SessionStorePath
```

The on-disk layout is:

```text
sessions/{sessionId}/
  session.json
  branches/
    {branchId}/
      branch.json
  uncommitted.json
  content/
    ...
```

`session.json` stores the `Session` object:

```ts
type SessionJson = {
  id: string;
  createdAt: string;
  lastActivity: string;
  metadata: Record<string, unknown>;
  middlewareState: Record<string, string>;
};
```

`branch.json` stores the branch event document, not the API `BranchDto` projection and not a direct `Branch` object:

```ts
type BranchJson = {
  schema: "hpd.agent.branch.events";
  version: 2;
  sessionId: string;
  branchId: string;
  createdAt: string;
  updatedAt: string;
  nextSequenceNumber: number;
  events: BranchEvent[];
};
```

`uncommitted.json` stores crash recovery state for the active in-flight turn. It is session-scoped and contains the active `branchId`.

```ts
type UncommittedTurnJson = {
  sessionId: string;
  branchId: string;
  turnId: string;
  iteration: number;
  completedFunctions: string[];
  middlewareState: unknown;
  isTerminated: boolean;
  terminationReason?: string | null;
  createdAt: string;
  lastUpdatedAt: string;
  version: number;
};
```

`uncommitted.json` is deleted when the message turn completes successfully. It is for crash recovery, not normal conversation hydration.

Branch persistence uses `SessionJsonContext.Combined`, `BranchEventDocumentJsonConverter`, and `AgentEventSerializer`. This keeps branch history AOT-compatible while preserving typed agent events and `Microsoft.Extensions.AI` / HPD custom content.

## Live Events vs Durable Branch Events

HPD-Agent has two related frontend data sources:

```text
live events:
  AgentEvent[] emitted during a run

durable history:
  BranchEvent[] loaded from the branch event log
```

They now share the same event protocol. Durable events add branch metadata such as `sequenceNumber`, `sessionId`, `branchId`, and `branchEventCategory`.

### Text Output

Live text arrives as:

```text
TEXT_MESSAGE_START
TEXT_DELTA
TEXT_MESSAGE_END
```

Durable text arrives through the same event names after commit. The stored `TEXT_DELTA` is coalesced, so it represents final text content rather than original token/chunk boundaries.

Reloaded history can reproduce the final text, but not the original streaming chunk boundaries or timing.

### Reasoning

Live reasoning arrives as:

```text
REASONING_MESSAGE_START
REASONING_DELTA
REASONING_MESSAGE_END
```

Durable reasoning also arrives through the same event names after commit. The stored `REASONING_DELTA` is coalesced, so it avoids raw reasoning-token spam while still preserving the reasoning artifact when the runtime observed reasoning content.

### Tool Calls

Live tool calls arrive as:

```text
TOOL_CALL_START
TOOL_CALL_ARGS
```

Durable tool calls arrive through the same `TOOL_CALL_START` and `TOOL_CALL_ARGS` names after commit.

Use `callId` as the primary correlation key. Preserve `messageId`, `harnessName`, `callType`, and trace/span fields when present.

### Tool Results

Live tool results arrive as:

```text
TOOL_CALL_END
TOOL_CALL_RESULT
```

Durable tool results arrive through the same `TOOL_CALL_END` and `TOOL_CALL_RESULT` event names after commit.

`TOOL_CALL_RESULT.result` uses normalized `ToolResultPayload`:

```ts
type ToolResultPayload = {
  text?: string;
  json?: unknown;
  content?: ToolResultContent[];
  resultType?: string;
};
```

The same payload shape is available from live and durable events. HPD-OS should render tool result cards from the event payload, not by scraping prose.

### Turn And Runtime Lifecycle

Live lifecycle events include:

- `MESSAGE_TURN_STARTED`;
- `MESSAGE_TURN_FINISHED`;
- `MESSAGE_TURN_ERROR`;
- `AGENT_TURN_STARTED`;
- `AGENT_TURN_FINISHED`;
- `STATE_SNAPSHOT`;
- `CHECKPOINT`;
- retry, continuation, interruption, and background-operation events.

These are runtime timeline events. They are not normal branch message content.

Some resulting state can be reflected in:

- `session.json`;
- `branch.json`;
- `uncommitted.json`;
- middleware state;
- branch metadata;
- committed messages.

But branch hydration should not be treated as a replay of lifecycle events.

### Middleware And Diagnostics

Live middleware and diagnostic events include:

- `MIDDLEWARE_STATE_SNAPSHOT`;
- `MIDDLEWARE_STATE_CHANGED`;
- `MIDDLEWARE_ERROR`;
- `HISTORY_REDUCTION`;
- retry events;
- circuit-breaker events;
- PII events;
- observability/debug events.

Persisted session or branch middleware state may contain the final durable state, but not the complete live sequence of state changes.

Render these events in live status/debug surfaces. Do not expect them to reappear as transcript messages during branch hydration.

### Human-In-The-Loop

Live interactive events include:

- `PERMISSION_REQUEST`;
- `PERMISSION_RESPONSE`;
- `PERMISSION_APPROVED`;
- `PERMISSION_DENIED`;
- `CONTINUATION_REQUEST`;
- `CONTINUATION_RESPONSE`;
- `CLARIFICATION_REQUEST`;
- `CLARIFICATION_RESPONSE`;
- `CLIENT_TOOL_INVOKE_REQUEST`;
- `CLIENT_TOOL_INVOKE_RESPONSE`.

These are runtime interaction events. Their effects may later appear indirectly in committed messages, for example a denied tool may produce a tool result, but the prompt lifecycle itself is live runtime state.

### Coding Harness Events

Live coding harness events include:

- command process started;
- command output chunks;
- command progress;
- command process exited;
- file edit/write events;
- language-server document and diagnostics events.

Persisted history usually contains the surrounding durable tool call/result events, plus any actual filesystem changes. It does not automatically preserve the full command-output/event timeline unless those coding harness events are explicitly persisted as branch events.

### Usage And Cost

Usage can appear on runtime events such as `MESSAGE_TURN_FINISHED`.

Usage and cost metadata should be treated as run metadata/evaluation data, not as ordinary transcript content.

### Projection Contract

Use this rule:

```text
live run:
  AgentEvent[] -> streaming transcript, tool cards, coding cards, prompts, status, debug timeline

reload:
  BranchEvent[] -> durable conversation history and persisted tool history
```

Frontend projection rules:

- Feed live `AgentEvent[]` and hydrated `BranchEvent[]` through the same app-owned projection model.
- Correlate tool calls and results by `callId`.
- Render `TOOL_CALL_RESULT.result` as the canonical tool result card payload for both live and hydrated events.
- Preserve unknown events and unknown `$type` content.
- Do not render diagnostics or tool results as assistant prose.
- Do not assume branch hydration can replay exact streaming deltas, command chunks, prompt lifecycles, or middleware timelines.

## Assets

Assets are scoped to a session and shared across branches.

Supported operations:

- upload asset;
- list assets;
- download asset;
- delete asset.

Assets are the right place for attachments, generated files, document inputs, images, audio, video, logs, and command-output artifacts.

## Transport

HPD-Agent exposes three useful transport levels:

```text
POST /agents/{agentId}/sessions/{sid}/branches/{bid}/stream
POST /agents/{agentId}/sessions/{sid}/branches/{bid}/events/stream
GET  /agents/{agentId}/sessions/{sid}/branches/{bid}/ws
```

The plain stream endpoint is useful for simple text output.

The raw event stream is the better default for HPD-OS chat because it exposes the full agent lifecycle.

The WebSocket endpoint is the strongest long-term fit when the UI needs bidirectional runtime interaction, interruption, permissions, clarification responses, and client tool responses on the same live connection.

## TypeScript Client SDK

HPD-OS already depends on the local HPD-Agent TypeScript SDK:

```text
@hpd/hpd-agent-client -> ../../HPD-AI-Framework/typescript/hpd-agent-client
```

This SDK should be treated as the first-class frontend infrastructure for HPD-Agent access. The chat UI should not reimplement HTTP routes, SSE parsing, response endpoint routing, or client-tool response handling unless the SDK is missing a capability.

Primary exports:

```ts
import {
  AgentClient,
  AgentHttpApi,
  ChatManager,
  ChatSession,
  ClientToolRegistry,
  EventTypes,
  SseParser,
  SseTransport,
  WebSocketTransport,
} from '@hpd/hpd-agent-client';
```

`AgentClient` is the main entry point. It owns:

- `api`: an `AgentHttpApi` for agents, sessions, branches, assets, and eval queries.
- `chat`: a `ChatManager` for opening scoped chat sessions.
- `tools`: a `ClientToolRegistry` for browser/client-side tool execution.
- typed event subscriptions with `on(type, handler)`.
- raw event subscriptions with `onAny(handler)`.
- transport errors with `onError(handler)`.
- `run(input, options)` for runtime input events.
- `start(scope)` / `stop()` for long-lived transport scope.
- `abort()` for active SSE runs.

The default transport is SSE:

```ts
const client = new AgentClient({ baseUrl: '/api/hpd-agent' });
```

WebSocket transport is available when HPD-OS wants a long-lived bidirectional runtime connection:

```ts
const client = new AgentClient({
  baseUrl: '/api/hpd-agent',
  transport: 'websocket',
});
```

### HTTP API Layer

`AgentHttpApi` already wraps the resource endpoints:

- `listAgents`, `getAgent`, `createAgent`, `updateAgent`, `deleteAgent`.
- `listSessions`, `searchSessions`, `getSession`, `createSession`, `updateSession`, `deleteSession`.
- `listBranches`, `getBranch`, `createBranch`, `forkBranch`, `deleteBranch`.
- `getBranchEvents`, `getBranchSiblings`, `getNextSibling`, `getPreviousSibling`.
- `uploadAsset`.
- eval query methods such as `getScores`, `getScoresByBranch`, `getEvaluatorSummary`, `getToolUsage`, and `getCost`.

The SDK also carries structured `AgentError` handling for HTTP and validation failures.

### Runtime Transport Layer

`SseTransport` already:

- posts `USER_TEXT_INPUT` to `/stream`;
- posts other input events to `/events/stream`;
- parses SSE with `SseParser`;
- handles UTF-8 chunk boundaries and multi-line `data:` fields;
- routes bidirectional responses to the dedicated response endpoints;
- exposes stale response conflicts as `AgentError` with code `STALE_RESPONSE`;
- supports abort signals.

`WebSocketTransport` already:

- connects to `/ws`;
- sends scoped input events with `agentId`, `sessionId`, and `branchId`;
- parses incoming JSON events;
- reports connection state.

### Client Tools

`ClientToolRegistry` is the frontend-side tool execution registry.

It supports:

- registering individual tools;
- registering a whole client harness/tool group;
- registering a fallback handler;
- normalizing harness-qualified tool names such as `browser.get_active_view` to `get_active_view`;
- automatically converting handler results into `CLIENT_TOOL_INVOKE_RESPONSE`.

Handler return values can be:

- a string, converted to text result content;
- a JSON value, converted to JSON result content;
- `ToolResultContent[]`;
- a full `ClientToolInvokeResponse`.

`AgentClient` automatically listens for `CLIENT_TOOL_INVOKE_REQUEST`, dispatches it through `client.tools`, and sends the response back through the active transport.

Client-tool run input is modeled as:

```ts
type AgentClientInput = {
  clientHarnesses?: ClientHarnessDefinition[];
  expandedContainers?: string[];
  hiddenTools?: string[];
  context?: ContextItem[];
  state?: unknown;
  metadata?: unknown;
  resetClientState?: boolean;
};
```

This means HPD-OS can expose UI state, active panes, selected files, browser/app actions, and route-specific tools to the agent without inventing a separate client-tool protocol.

### Chat Layer

`ChatManager` and `ChatSession` provide a small scoped convenience layer:

```ts
const chat = await client.chat.open({
  agentId: 'assistant',
  branchId: 'main',
  session: {
    search: { metadata: { workspaceId: 'hpd-os' } },
    create: { metadata: { workspaceId: 'hpd-os' } },
  },
});

await chat.getBranchEvents();
await chat.sendText('Hello', { runConfig });
```

`ChatSession` intentionally leaves transcript rendering to event handlers and branch event hydration. It is a scope wrapper, not a full UI model.

### Type Coverage

The SDK includes frontend protocol types for:

- agent definitions;
- sessions, branches, sibling branches, branch events, and assets;
- run config;
- evals;
- client tools;
- runtime transports;
- known HPD-Agent events;
- unknown/custom events.

Its `BranchEvent` type is `AgentEvent` plus branch metadata. Durable transcript events and live runtime events therefore share the same frontend event protocol. Unknown content and unknown events are preserved.

The SDK event model preserves unknown runtime events through `UnknownAgentEvent`. HPD-OS should continue to keep unknown events visible in a debug/event timeline rather than dropping them.

### Projection Boundary

The SDK intentionally does not ship an opinionated conversation reducer. The right boundary is:

```text
use @hpd/hpd-agent-client for API, transport, protocol types, errors, chat scope, and client tools
build HPD-OS-owned projection state for transcript, coding activity, prompts, and debug timeline
```

This keeps HPD-Agent generic while letting HPD-OS choose its own chat, automation, coding, and debug timeline product model.

## Input Events

The runtime accepts typed input events.

Primary input event types:

- `USER_TEXT_INPUT`
- `USER_MESSAGES_INPUT`
- `INTERRUPTION_REQUEST`

The friendly text endpoint wraps text as `USER_TEXT_INPUT`.

The raw event endpoint accepts serialized `AgentInputEvent` JSON.

The WebSocket can carry input events and bidirectional response events.

`USER_MESSAGES_INPUT` is the richer input shape. It can carry a list of chat messages and typed content parts.

Known content registrations include:

- `hpd:image`
- `hpd:audio`
- `hpd:video`
- `hpd:document`

`AgentRunConfig.Attachments` is not a JSON wire field. For frontend uploads, prefer session assets and typed message content rather than assuming `attachments` can be posted directly in run config JSON.

## Run Config

The UI can control useful per-run behavior through `AgentRunConfig` and nested chat config.

Useful wire-facing fields include:

- chat model settings;
- provider key;
- model id;
- API key and provider endpoint when allowed;
- custom provider headers;
- system instruction overrides;
- additional system instructions;
- context overrides;
- run timeout;
- cache behavior;
- tool skipping;
- delta coalescing;
- permission overrides;
- client tool input;
- conversation id override;
- background response behavior;
- history reduction behavior;
- structured output.

Useful chat model fields include:

- `temperature`;
- `topP`;
- `topK`;
- `maxOutputTokens`;
- `frequencyPenalty`;
- `presencePenalty`;
- `modelId`;
- `stopSequences`;
- `reasoning`;
- `additionalProperties`.

Some runtime-only fields are intentionally not JSON wire fields. The frontend should not depend on them.

## Output Event Envelope

Agent output is serialized as typed events. The discriminator is `type`, using SCREAMING_SNAKE_CASE names such as:

```json
{ "version": "1.0", "type": "TEXT_DELTA", "text": "hello", "messageId": "msg-123" }
```

Events inherit common HPD event metadata:

- `channel`;
- `kind`;
- `direction`;
- `sequenceNumber`;
- `eventFlowId`;
- `canInterrupt`;
- `timestamp`;
- `exchangeTimestampNs`;
- `extensions`.

Agent events can also carry:

- `executionContext`;
- `traceId`;
- `spanId`;
- `parentSpanId`.

These fields make it possible to order events, group event flows, trace nested/sub-agent work, and build debug timelines.

## Event Channels

HPD events classify transport behavior with channels:

- `Streaming`: high-throughput data where old items may be skipped or coalesced.
- `Synchronous`: causally ordered FIFO work.
- `Interactive`: user-facing interactions that must remain responsive and ordered.
- `Control`: interruptions, cancellations, and circuit-breaker signals.

The UI should use these as hints for rendering and prioritization.

## Event Kinds

HPD events classify purpose with kinds:

- `Lifecycle`: started, stopped, completed.
- `Content`: text, data, results.
- `Control`: permissions, interruptions, user input.
- `Diagnostic`: telemetry and debugging information.

The UI should not render every diagnostic event in the main conversation. Most diagnostics belong in an expandable timeline.

## Core Output Events

Message-turn lifecycle:

- `MESSAGE_TURN_STARTED`
- `MESSAGE_TURN_FINISHED`
- `MESSAGE_TURN_ERROR`

Agent-turn lifecycle:

- `AGENT_TURN_STARTED`
- `AGENT_TURN_FINISHED`
- `STATE_SNAPSHOT`

Assistant text:

- `TEXT_MESSAGE_START`
- `TEXT_DELTA`
- `TEXT_MESSAGE_END`

Reasoning:

- `REASONING_MESSAGE_START`
- `REASONING_DELTA`
- `REASONING_MESSAGE_END`

Tool calls:

- `TOOL_CALL_START`
- `TOOL_CALL_ARGS`
- `TOOL_CALL_END`
- `TOOL_CALL_RESULT`
- `TOOL_CALL_BACKGROUND_TASK_STARTED`
- `TOOL_CALL_BACKGROUND_TASK_COMPLETED`
- `TOOL_CALL_BACKGROUND_TASK_CANCELLED`
- `TOOL_CALL_BACKGROUND_TASK_FAULTED`

### Tool Call Result Mechanism

Tool results now have one frontend event representation across live and durable history:

```text
live event stream:
  TOOL_CALL_RESULT.result -> ToolResultPayload

hydrated branch history:
  TOOL_CALL_RESULT.result -> ToolResultPayload
```

This is the main DX improvement from the branch event-source update. HPD-OS no longer needs a separate "live tool result" path and "hydrated functionResult content" path for normal branch history.

During tool execution, HPD-Agent creates a `FunctionExecutionOutcome` for each call. That outcome carries both:

- `Result`: the raw object used by model history internally;
- `ResultPayload`: a normalized, event-facing `ToolResultPayload`.

`ToolResultPayload` is intentionally shaped for UI and transport:

```ts
type ToolResultPayload = {
  text?: string;
  json?: unknown;
  content?: ToolResultContent[];
  resultType?: string;
};
```

The normalization rules matter:

- string results expose both `text` and JSON string form;
- JSON results expose raw JSON and text fallback;
- validation errors expose structured JSON;
- client-tool text/json/binary content is preserved as `content`;
- arbitrary objects fall back to `text = result.toString()` plus `resultType`;
- `null` is represented as JSON null.

The execution result returned from the function processor carries:

```text
ChatMessage toolResultMessage
HashSet<string> successfulFunctions
IReadOnlyDictionary<string, ToolResultPayload> resultPayloads
```

The dictionary is keyed by `callId`. After execution:

1. HPD-Agent builds a tool-role `ChatMessage` containing model-facing function result content.
2. Middleware can inspect the tool results through `AfterIterationContext.ToolResults`.
3. The tool result message is added to shared messages and turn history.
4. The branch commit converts the committed message into durable branch events.
5. The durable branch event log stores `TOOL_CALL_RESULT` as an `AgentEvent` with `ToolResultPayload`.
6. Live SSE/WebSocket also emits `TOOL_CALL_RESULT` as an `AgentEvent` with `ToolResultPayload`.

Every committed function result should therefore have a corresponding normalized durable tool result event.

This gives HPD-OS one clean projection path:

```text
live run:
  TOOL_CALL_START / TOOL_CALL_ARGS / TOOL_CALL_END / TOOL_CALL_RESULT
    -> tool activity cards, status, args, normalized result preview

reload:
  same event names from getBranchEvents()
    -> same tool activity cards, completed status, args, normalized result preview
```

`TOOL_CALL_RESULT` preserves `text`, `json`, `content`, `resultType`, `harnessName`, and `callType` when available.

Projection rule:

- Use `callId` as the primary correlation key.
- Use `TOOL_CALL_RESULT.result` for both live and hydrated result UI.
- Preserve `harnessName` and `callType` when available.
- Never render raw tool results as assistant prose.

Human-in-the-loop and bidirectional control:

- `PERMISSION_REQUEST`
- `PERMISSION_RESPONSE`
- `PERMISSION_APPROVED`
- `PERMISSION_DENIED`
- `CONTINUATION_REQUEST`
- `CONTINUATION_RESPONSE`
- `CLARIFICATION_REQUEST`
- `CLARIFICATION_RESPONSE`
- `CLIENT_TOOL_INVOKE_REQUEST`
- `CLIENT_TOOL_INVOKE_RESPONSE`
- `CLIENT_TOOL_GROUPS_REGISTERED`

Middleware and runtime diagnostics:

- `MIDDLEWARE_ERROR`
- `HISTORY_REDUCTION`
- `MAX_CONSECUTIVE_ERRORS_EXCEEDED`
- `TOTAL_ERROR_THRESHOLD_EXCEEDED`
- `PII_DETECTED`
- `CHECKPOINT`
- `INTERNAL_RETRY`
- `FUNCTION_RETRY`
- `MODEL_CALL_RETRY`
- `PLAN_MODE_ACTIVATED`
- `PLAN_UPDATED`
- `NESTED_AGENT_INVOKED`
- `EVENT_DROPPED`
- `BACKGROUND_OPERATION_STARTED`
- `BACKGROUND_OPERATION_STATUS`
- `STRUCTURED_OUTPUT_START`
- `STRUCTURED_OUTPUT_PARTIAL`
- `STRUCTURED_OUTPUT_COMPLETE`
- `STRUCTURED_OUTPUT_ERROR`
- `ASSET_UPLOADED`
- `ASSET_UPLOAD_FAILED`

Streaming and interruption:

- `INTERRUPTION_REQUEST`
- `INTERRUPTION_HANDLED`

## Coding Harness Events

The coding harness registers additional agent events. These should be treated as first-class HPD-OS UI material, not as plain markdown.

Command execution:

- durable: `EXECUTE_COMMAND_PROCESS_STARTED`
- live-only: `EXECUTE_COMMAND_OUTPUT_CHUNK`
- live-only: `EXECUTE_COMMAND_PROGRESS`
- durable: `EXECUTE_COMMAND_PROCESS_EXITED`
- durable: `EXECUTE_COMMAND_AUTO_BACKGROUNDED`
- live-only: `EXECUTE_COMMAND_BACKGROUND_LIST`

File mutations:

- `FILE_EDIT_APPLIED`
- `FILE_WRITE_APPLIED`

Language server:

- `LANGUAGE_SERVER_DOCUMENT_OPENED`
- `LANGUAGE_SERVER_DOCUMENT_CHANGED`
- `LANGUAGE_SERVER_DOCUMENT_CLOSED`
- `LANGUAGE_SERVER_DOCUMENT_SAVED`
- `LANGUAGE_SERVER_WATCHED_FILE_CHANGED`
- `LANGUAGE_SERVER_DIAGNOSTICS_RECEIVED`

These events let HPD-OS render:

- command cards;
- live command output;
- command status and duration;
- backgrounded command state;
- file edit summaries;
- diff stats;
- write summaries;
- diagnostics.

## Human-In-The-Loop Response Endpoints

When the runtime emits interactive requests, the UI can respond through dedicated endpoints:

```text
POST /agents/{agentId}/sessions/{sid}/branches/{bid}/permissions/respond
POST /agents/{agentId}/sessions/{sid}/branches/{bid}/continuation/respond
POST /agents/{agentId}/sessions/{sid}/branches/{bid}/clarifications/respond
POST /agents/{agentId}/sessions/{sid}/branches/{bid}/client-tools/respond
```

The chat UI should model these as pending prompts attached to the active run.

## Recommended Frontend Architecture

Do not build HPD-OS chat as a "messages only" UI.

Build it as:

```text
HPD-Agent transport
    -> raw AgentEvent log
    -> event reducer
    -> chat projection
    -> tool/coding projection
    -> interactive prompt projection
    -> debug timeline
```

The raw event log should preserve unknown events. Unknown events must not break the UI.

The main chat projection should render only what belongs in the conversation:

- user messages;
- assistant text;
- reasoning blocks;
- compact tool/coding activity;
- pending permission, continuation, clarification, and client-tool prompts;
- user-facing errors.

The debug timeline can expose everything else.

## Suggested Frontend Files

When implementing the event side, prefer a dedicated chat event layer:

```text
svelte/chat/events/agentEventTypes.ts
svelte/chat/events/agentEventParser.ts
svelte/chat/events/eventReducer.ts
svelte/chat/events/chatProjection.ts
svelte/chat/events/codingProjection.ts
```

Responsibilities:

- `agentEventTypes.ts`: frontend event type names and minimal event shapes.
- `agentEventParser.ts`: SSE/WebSocket JSON parsing and unknown-event preservation.
- `eventReducer.ts`: raw event log to normalized runtime state.
- `chatProjection.ts`: normalized state to message blocks.
- `codingProjection.ts`: normalized state to command/file/diagnostic UI blocks.

## Design Rules

- Preserve raw events.
- Project events into UI state.
- Do not make backend event classes equal to Svelte component state.
- Do not render every diagnostic event in the main chat.
- Treat coding harness events as native UI, not markdown.
- Keep unknown events visible in a debug timeline.
- Prefer raw event streaming or WebSocket for the real app.
- Use the friendly text stream only for prototypes.

## Bottom Line

HPD-Agent already gives HPD-OS the infrastructure for a real agent IDE surface:

```text
workspace/session/branch
    -> user input
    -> event stream
    -> chat projection
    -> tool/coding projection
    -> permission/clarification UI
    -> asset/file/diagnostic views
```

The frontend should build around event projection from the beginning. That avoids a retrofit from "chat messages" into "agent runtime UI" later.
