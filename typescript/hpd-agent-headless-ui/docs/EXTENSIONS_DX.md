# Thread Event Extensions DX

Thread event extensions let an application project its own backend events into
typed headless UI state without regenerating the TypeScript SDK for every event.

Use them when:

- a backend library, tenant, or application emits a concrete custom event;
- the backend emits `RUNTIME_AGENT_LIVE_EVENT` or
  `RUNTIME_AGENT_DURABLE_EVENT`;
- the event enriches an existing tool call, work group, or side panel;
- the event needs application-specific decoding or aggregation.

Do not use an extension to duplicate core behavior already covered by text,
reasoning, tool-call, runtime-request, subagent, or thread-execution events.

## Mental Model

```text
backend event
    -> existing or unknown wire JSON
    -> ThreadController
    -> matching ThreadEventExtension reducers
    -> projection.extensions[key]
    -> application-owned UI
```

The generated SDK remains the contract for stable HPD APIs and known core
events. Extensions are the forward-compatible layer for application event
semantics.

The headless core deliberately does not decide whether a custom event is a
message, work row, modal, inspector item, or hidden state. The application owns
that presentation policy.

## Event Categories

There are two practical extension sources.

### Concrete backend events

A library can define a concrete event:

```csharp
[DurableEvent]
[EventType("ANALYSIS_COMPLETED")]
public sealed record AnalysisCompletedEvent(
    string DatasetId,
    int RowCount) : AgentEvent;
```

The backend source generator includes the event in the consuming application's
event composition and generates its serialization metadata.

An older client SDK may not contain `ANALYSIS_COMPLETED` in its generated union.
The event still reaches `onUnknownEvent`, and the headless controller forwards it
to matching extensions.

### Runtime-extension envelopes

Tenant/application-defined schemas use stable envelopes:

```text
RUNTIME_AGENT_LIVE_EVENT
RUNTIME_AGENT_DURABLE_EVENT
```

Their `payload` is schema-defined. Match the namespace, name, and version in the
reducer.

## Basic Reducer

```ts
import type { ThreadEventExtension } from '@hpd-research/hpd-agent-headless-ui';

interface AnalysisState {
  datasetId: string | null;
  completedRows: number;
}

const analysisEvents: ThreadEventExtension<AnalysisState> = {
  key: 'analysis',
  initialState: {
    datasetId: null,
    completedRows: 0,
  },
  matches: event => event.type === 'ANALYSIS_COMPLETED',
  reduce: (state, event) => {
    const value = event as Record<string, unknown>;
    return {
      datasetId: typeof value.datasetId === 'string'
        ? value.datasetId
        : state.datasetId,
      completedRows: typeof value.rowCount === 'number'
        ? value.rowCount
        : state.completedRows,
    };
  },
};
```

Register the reducer when creating the projection or controller:

```ts
const thread = createThreadController({
  client,
  agentId,
  sessionId,
  threadId,
  extensions: [analysisEvents],
});
```

Read the projected state:

```ts
const snapshot = thread.projection.getSnapshot();
const analysis = snapshot.extensions.analysis as AnalysisState;
```

`getThreadExtensionState<T>()` provides the same typed lookup:

```ts
const analysis = getThreadExtensionState<AnalysisState>(
  thread.projection.getSnapshot(),
  'analysis',
);
```

## Runtime Extension Example

```ts
const retrievalProgress: ThreadEventExtension<{ percent: number }> = {
  key: 'acme:retrieval-progress@1',
  initialState: { percent: 0 },
  matches: event =>
    event.type === 'RUNTIME_AGENT_LIVE_EVENT' ||
    event.type === 'RUNTIME_AGENT_DURABLE_EVENT',
  reduce: (state, event) => {
    const value = event as Record<string, unknown>;
    if (value.extensionNamespace !== 'acme' ||
        value.schemaName !== 'retrieval-progress' ||
        value.schemaVersion !== 1) {
      return state;
    }

    const payload = value.payload as { percent?: unknown };
    return {
      percent: typeof payload?.percent === 'number'
        ? payload.percent
        : state.percent,
    };
  },
};
```

Use the schema key and version as the state identity. If version 2 changes the
payload shape, add a version-2 reducer key rather than silently reinterpreting
version-1 data.

## Coding Harness Worked Example

The coding harness emits concrete events such as:

- `FILE_EDIT_APPLIED`
- `FILE_WRITE_APPLIED`
- `EXECUTE_COMMAND_PROCESS_STARTED`
- `EXECUTE_COMMAND_PROGRESS`
- `EXECUTE_COMMAND_OUTPUT_CHUNK`
- `EXECUTE_COMMAND_PROCESS_EXITED`
- `LANGUAGE_SERVER_DIAGNOSTICS_RECEIVED`

The current TypeScript SDK does not have to know these discriminators. The
extension can recognize them by name.

```ts
interface CodingState {
  commands: Record<string, {
    toolCallId: string;
    command: string;
    status: 'running' | 'exited';
    elapsedMs: number;
    stdoutBytes: number;
    stderrBytes: number;
    exitCode?: number | null;
  }>;
  edits: Record<string, {
    path: string;
    addedLines: number;
    removedLines: number;
  }>;
}

const codingEvents: ThreadEventExtension<CodingState> = {
  key: 'hpd:coding',
  initialState: {
    commands: {},
    edits: {},
  },
  matches: event =>
    event.type === 'FILE_EDIT_APPLIED' ||
    event.type === 'FILE_WRITE_APPLIED' ||
    event.type.startsWith('EXECUTE_COMMAND_'),
  reduce: (state, event) => {
    const value = event as Record<string, unknown>;

    if (event.type === 'EXECUTE_COMMAND_PROCESS_STARTED') {
      const commandId = String(value.commandId);
      return {
        ...state,
        commands: {
          ...state.commands,
          [commandId]: {
            toolCallId: String(value.toolCallId),
            command: String(value.command),
            status: 'running',
            elapsedMs: 0,
            stdoutBytes: 0,
            stderrBytes: 0,
          },
        },
      };
    }

    if (event.type === 'EXECUTE_COMMAND_PROGRESS') {
      const commandId = String(value.commandId);
      const command = state.commands[commandId];
      if (!command) return state;

      return {
        ...state,
        commands: {
          ...state.commands,
          [commandId]: {
            ...command,
            elapsedMs: Number(value.elapsedMilliseconds),
            stdoutBytes: Number(value.stdoutBytes),
            stderrBytes: Number(value.stderrBytes),
          },
        },
      };
    }

    if (event.type === 'EXECUTE_COMMAND_PROCESS_EXITED') {
      const commandId = String(value.commandId);
      const command = state.commands[commandId];
      if (!command) return state;

      return {
        ...state,
        commands: {
          ...state.commands,
          [commandId]: {
            ...command,
            status: 'exited',
            exitCode: typeof value.exitCode === 'number' ? value.exitCode : null,
          },
        },
      };
    }

    if (event.type === 'FILE_EDIT_APPLIED' ||
        event.type === 'FILE_WRITE_APPLIED') {
      const toolCallId = String(value.toolCallId);
      const diffStat = value.diffStat as {
        addedLines: number;
        removedLines: number;
      } | undefined;

      return {
        ...state,
        edits: {
          ...state.edits,
          [toolCallId]: {
            path: String(value.displayPath ?? value.path ?? ''),
            addedLines: diffStat?.addedLines ?? 0,
            removedLines: diffStat?.removedLines ?? 0,
          },
        },
      };
    }

    return state;
  },
};
```

Register it:

```ts
const thread = createThreadState({
  client,
  agentId,
  sessionId,
  threadId,
  extensions: [codingEvents],
});
```

The reducer is synchronous. Decode and aggregate the event here; perform
asynchronous inspector loading or network work in the presentation layer.

## Correlating With Core Tool Events

Coding harness events enrich the normal tool-call lifecycle:

```text
TOOL_CALL_START.callId
    === FILE_EDIT_APPLIED.toolCallId
    === EXECUTE_COMMAND_*.toolCallId

EXECUTE_COMMAND_*.commandId
    groups one command lifecycle
```

Keep the core tool call as the primary activity identity and extension state as
the detail projection:

```ts
const tool = activeTool;
const edit = coding.edits[tool.callId];
const command = Object.values(coding.commands)
  .find(candidate => candidate.toolCallId === tool.callId);
```

An owned-tool presentation policy may either add details below the generic
tool call or replace that generic row. Use replacement when the specialized card
already conveys the command, mutation, and result; otherwise users see the same
operation twice.

```svelte
{#snippet toolPart({ part, props })}
  {#if ownsCodingTool(part.tool, coding)}
    <CodingToolDetails state={coding} toolCallId={part.tool.callId} />
  {:else}
    <ToolCall tool={part.tool} {...props} />
  {/if}
{/snippet}
```

Ownership must match the tool harness and identity, with a generic fallback when
the specialized projection is unavailable. This mirrors the Agent TUI's active
tool-call handler rule.

Do not append a second transcript message for every coding event. Use:

- `ThreadWorkGroup.toolPart` for owned command and edit presentation;
- a generic `ToolCall` fallback for unowned tools;
- `DiffViewer` for mutation hunks;
- a side panel or inspector for full command output;
- a diagnostics panel keyed by file path;
- a compact progress row inside the existing work group.

## Live And Durable Events

The extension API preserves backend durability semantics.

### Durable

Durable events replay after reconnect and after a replacement generation. The
reducer must therefore be deterministic and idempotent when keyed by stable ids:

```ts
commands[commandId] = ...
edits[toolCallId] = ...
diagnostics[path] = ...
```

Do not append the same durable event again when the reducer is replayed.

### Live-only

`EXECUTE_COMMAND_OUTPUT_CHUNK` is live-only. It may be displayed while the client
is connected, but it cannot restore output after refresh or hard compaction.

For buffered output, use a bounded append:

```ts
const existing = state.commands[commandId]?.liveOutput ?? '';
const liveOutput = `${existing}${String(value.text ?? '')}`.slice(-64_000);
```

Never use a live-only chunk as the sole source for durable command completion.
Use `EXECUTE_COMMAND_PROCESS_EXITED`, which is durable.

## Replay And Rebase

Extensions run through the same projection path for:

- initial journal replay;
- live SSE delivery;
- finite rehydration with supplied events;
- replacement-generation replay after hard compaction.

On rehydration or journal rebase, extension state returns to `initialState` and
is rebuilt from the replacement journal. This prevents stale state from the old
generation leaking into the new one.

If an extension throws, the event was not successfully applied. The stream stops
at that event so the client cannot advance past a projection it did not accept.

## Svelte DX

`createThreadState()` exposes extension state directly:

```svelte
<script lang="ts">
  import {
    ThreadConversation,
    type ThreadEventExtension,
  } from '@hpd-research/hpd-agent-headless-ui-svelte';

  const thread = createThreadState({
    client,
    agentId,
    sessionId,
    threadId,
    extensions: [codingEvents],
  });
</script>

<ThreadConversation {thread}>
  {#snippet header({ snapshot })}
    {@const coding = snapshot.extensions['hpd:coding'] as CodingState}
    <span>{Object.keys(coding.commands).length} commands</span>
    <span>{Object.keys(coding.edits).length} edits</span>
  {/snippet}
</ThreadConversation>
```

The adapter does not automatically render extension state. Render it through
region snippets, custom tool snippets, or application components.

## Custom Projection Interaction

When passing a custom `projection` to `createThreadController()`, configure
extensions on that projection:

```ts
const projection = createThreadProjection({
  extensions: [codingEvents],
});

const controller = createThreadController({
  client,
  agentId,
  sessionId,
  threadId,
  projection,
});
```

Do not pass both a preconfigured `projection` and a separate `extensions` array
to the controller.

## SDK Generation Boundary

An old SDK can consume new application events:

- unknown concrete event types reach `onUnknownEvent`;
- runtime-extension envelopes have stable generated envelope types;
- the headless controller forwards both to matching extensions.

Regenerate the SDK when:

- a REST route or request/response DTO changes;
- you want compile-time types for a newly integrated application event;
- the host's generated OpenAPI event union should include the event.

Do not regenerate the SDK merely because one application event payload changed.
Version the payload contract and update the corresponding extension reducer.

## Authoring Rules

- Give each extension a stable, unique `key`.
- Match the narrowest event set possible.
- Validate every unknown payload field before using it.
- Keep reducers deterministic, synchronous, and immutable.
- Correlate by backend ids rather than transcript row numbers.
- Preserve absolute ordering only through `snapshot.timeline`.
- Bound arrays and streamed text.
- Treat event payload HTML, URLs, paths, and commands as untrusted UI input.
- Keep custom events out of transcript semantics unless explicitly intended.
- Test live and replayed delivery through the same reducer.
