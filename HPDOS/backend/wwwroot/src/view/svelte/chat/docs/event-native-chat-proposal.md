# HPD-OS Event-Native Chat Proposal

**Document:** `event-native-chat-proposal.md`  
**Status:** Proposal  
**Date:** May 2026  
**Applies To:** HPD-OS chat workspace, workspace-scoped sessions, agent runtime UI, branch hydration, tool rendering  
**Depends On:** `@hpd/hpd-agent-client`, HPD-Agent branch events, Svelte 5, Tailwind CSS v4  

## Executive Summary

HPD-OS should build chat as an event-native runtime surface, not as a message list with tool decorations.

Chat sessions should be scoped to the active HPD-OS workspace. The sidebar owns the workspace session list and active session selection. The chat pane owns rendering the selected session branch. This mirrors opencode's useful workspace/session separation, but uses HPD's architecture: workspace identity is HPD-OS session metadata, while the coding harness receives explicit multi-root workspace context through `runConfig.contextOverrides.workspace`.

The central invariant:

```text
BranchEvent[] from durable branch history
AgentEvent[] from live stream
    -> same HPD-OS projector
    -> same UI timeline model
```

This is the second-mover advantage. opencode shows the UI categories a coding agent needs: transcript, reasoning, tools, diffs, permissions, questions, tasks, and session controls. HPD-Agent now gives HPD-OS a stronger substrate: durable branch events and live runtime events share the same frontend protocol.

So HPD-OS should not copy opencode's `Message + Part[]` compatibility model. It should use the HPD-Agent TypeScript client for transport and API access, then own a small projection layer that turns raw events into product-specific timeline items.

## Goals

- Use `@hpd/hpd-agent-client` as the only HPD-Agent API and transport boundary.
- Keep the session list in the shell/sidebar and scope it by HPD-OS workspace metadata.
- Create HPD-OS sessions with stable workspace metadata.
- Hydrate durable branch history with `getBranchEvents()`.
- Stream live agent events through the same projection path.
- Render chat as semantic timeline cards, not raw event dumps.
- Inject the current HPD-OS multi-root workspace into every run through `runConfig.contextOverrides.workspace`.
- Add concise workspace instructions to every run through `runConfig.additionalSystemInstructions`.
- Keep layout code separate from agent runtime code.
- Keep unknown events visible in a debug timeline.
- Make reload and live streaming converge on the same final UI.
- Use Svelte 5 runes for local reactive state instead of global stores by default.
- Use Tailwind CSS v4 and small scoped CSS only where timeline/card styling needs shared selectors.

## Non-Goals

- Do not copy opencode's `Message + Part[]` store.
- Do not put chat runtime logic in layout controllers.
- Do not make `ChatWorkspace.svelte` responsible for event projection.
- Do not hand-roll fetch calls throughout Svelte components.
- Do not flatten HPD-Agent content too early.
- Do not make the chat transcript own session discovery or workspace selection.
- Do not use display names as session identity keys.
- Do not treat model-visible workspace instructions as the coding harness sandbox.
- Do not require every diagnostic/runtime event to appear in the main chat transcript.
- Do not build the full composer feature set before the event projection spine exists.

## Current Code Shape

The current HPD-OS chat code is mostly layout scaffolding:

```text
chat/
  ChatWorkspace.svelte
  controller.ts
  layout.ts
  resize.svelte.ts
  storage.ts
  components/
    ChatWorkspacePane.svelte
    ChatAppPane.svelte
    ChatResizeHandle.svelte
  docs/
    hpd-agent-infrastructure.md
```

This is good. There is no stale chat runtime architecture to unwind yet.

`ChatWorkspace.svelte` owns the split-pane composition. `controller.ts`, `layout.ts`, `resize.svelte.ts`, and `storage.ts` own layout measurement, resize behavior, and persisted pane widths. They should stay focused on layout.

The missing layer is a chat runtime/projection module.

The missing workspace layer is a sidebar-owned session controller. `ShellSidebar.svelte` currently renders route navigation only. The chat feature should add a workspace-scoped session list there, while keeping `ChatWorkspacePane.svelte` focused on the selected conversation surface.

## Proposed Module Shape

Add a dedicated chat runtime and timeline module:

```text
chat/
  runtime/
    agentClient.ts
    workspaceContext.ts
    chatSessions.svelte.ts
    chatSession.svelte.ts
    chatProjector.ts
    chatTimeline.ts
    chatTypes.ts
  components/
    ChatTimeline.svelte
    ChatComposer.svelte
    cards/
      TextCard.svelte
      ReasoningCard.svelte
      ToolCard.svelte
      CommandCard.svelte
      FileEditCard.svelte
      PermissionCard.svelte
      ClarificationCard.svelte
      UnknownEventCard.svelte
```

The existing layout components remain:

```text
chat/
  ChatWorkspace.svelte
  components/
    ChatWorkspacePane.svelte
    ChatAppPane.svelte
    ChatResizeHandle.svelte
```

The boundary:

```text
layout files
    know about panes, widths, shell/sidebar mode

runtime files
    know about HPD-Agent client, workspace metadata, sessions, branches, live events, projection

timeline components
    know about ChatTimelineItem and card rendering
```

## TypeScript Client Boundary

HPD-OS should use `@hpd/hpd-agent-client` for:

- agent CRUD;
- session CRUD/search;
- branch list/read/create/fork/update/delete;
- branch event hydration;
- sibling branch navigation;
- assets;
- live SSE/WebSocket transport;
- permission responses;
- continuation responses;
- clarification responses;
- client tool responses;
- protocol types such as `AgentEvent`, `BranchEvent`, and `ToolResultPayload`.

HPD-OS should not scatter raw calls to `/api/hpd-agent` through Svelte components.

Recommended shape:

```ts
import { AgentClient } from "@hpd/hpd-agent-client";

export function createHpdAgentClient() {
  return new AgentClient({
    baseUrl: "/api/hpd-agent"
  });
}
```

That wrapper exists only to centralize app defaults. It should not become a second SDK.

## Workspace-Scoped Sessions

HPD-OS sessions should be scoped by stable workspace metadata. opencode stores `projectID`, optional `workspaceID`, and `directory` directly on session records. HPD-Agent is intentionally host-agnostic, so HPD-OS should express the same ownership through session metadata.

Every HPD-OS-created chat session should include:

```ts
export type HpdosSessionMetadata = {
  app: "hpd-os";
  workspaceId: string;
  defaultRootId: string;
  defaultRootPath: string;
};
```

Optional display metadata can be included, but must not be used for identity:

```ts
{
  workspaceName?: string;
  defaultRootLabel?: string;
}
```

The sidebar should load sessions with:

```ts
const sessions = await client.searchSessions({
  metadata: {
    app: "hpd-os",
    workspaceId,
    defaultRootId,
    defaultRootPath
  },
  limit: 50
});
```

New sessions should be created with the same metadata:

```ts
const session = await client.createSession({
  metadata: {
    app: "hpd-os",
    workspaceId,
    defaultRootId,
    defaultRootPath
  }
});
```

Use `searchSessions`, not `listSessions`, for workspace filtering. `listSessions` is for unscoped listing/pagination; workspace ownership is metadata and belongs in `SearchSessionsRequest`.

## Sidebar Ownership

The session list belongs in the sidebar, not inside the chat transcript.

The shell/sidebar owns:

- active workspace;
- workspace root summary;
- workspace-scoped session list;
- create session;
- delete/archive session when supported;
- active session selection.

The chat route owns:

- active `sessionId`;
- active `branchId`;
- branch event hydration;
- live event streaming;
- timeline projection;
- composer submission.

The boundary:

```text
Shell/sidebar
    -> selects workspace session
    -> passes sessionId + branchId

Chat route
    -> hydrates that branch
    -> renders that branch
    -> sends messages to that branch
```

This keeps the chat surface honest: it displays what is in the selected session. It does not decide which workspace owns the session.

## Workspace Runtime Context

HPD-OS workspaces are multi-root. A workspace has one default root and may include additional roots selected by the user.

The coding harness expects workspace access through `runConfig.contextOverrides.workspace`:

```ts
export type HpdosRunWorkspace = {
  version: 1;
  defaultRootId: string;
  roots: HpdosRunWorkspaceRoot[];
};

export type HpdosRunWorkspaceRoot = {
  id: string;
  path: string;
  label?: string;
};
```

Example:

```ts
const workspaceContext = {
  version: 1,
  defaultRootId: "default",
  roots: [
    {
      id: "default",
      label: "HPD-OS",
      path: "/Users/ewoof/Desktop/HPD-OS"
    },
    {
      id: "docs",
      label: "Docs",
      path: "/Users/ewoof/Desktop/HPD-Agent-InternalDocs"
    }
  ]
};
```

Every HPD-OS chat run should send:

```ts
await chat.sendText(draft.text, {
  runConfig: {
    additionalSystemInstructions: buildWorkspaceInstructions(workspaceContext),
    contextOverrides: {
      workspace: workspaceContext
    }
  }
});
```

The three workspace layers must stay separate:

```text
session metadata
    = which HPD-OS workspace owns this session

additionalSystemInstructions
    = what the model should know about the workspace

contextOverrides.workspace
    = what coding tools are allowed to touch
```

Do not rely on `additionalSystemInstructions` for sandboxing. The coding harness enforces path access from `contextOverrides.workspace`.

The model-facing workspace instructions should be concise. They should name the active workspace, the default root, and any additional root aliases:

```text
Current HPD-OS workspace:
- Workspace: HPD-OS
- Default root: /Users/ewoof/Desktop/HPD-OS
- Additional roots:
  - @docs => /Users/ewoof/Desktop/HPD-Agent-InternalDocs
Use root-qualified paths such as @docs/... for non-default roots.
```

The future workspace manager UI can own folder picking, root labels, root ids, and root display. The chat runtime should assume the multi-root shape from day one.

## Chat Session State

Create one state object per active chat workspace:

```ts
import type { AgentEvent, BranchEvent, ChatSession } from "@hpd/hpd-agent-client";
import { projectChatEvents } from "./chatProjector";

export class ChatSessionState {
  readonly chat: ChatSession;
  readonly workspace: HpdosRunWorkspace;

  events = $state<(AgentEvent | BranchEvent)[]>([]);
  hydrated = $state(false);
  streaming = $state(false);
  error = $state<string | null>(null);

  timeline = $derived(projectChatEvents(this.events));

  constructor(chat: ChatSession, workspace: HpdosRunWorkspace) {
    this.chat = chat;
    this.workspace = workspace;
  }

  async hydrate() {
    this.hydrated = false;
    const events = await this.chat.getBranchEvents();
    this.events = events;
    this.hydrated = true;
  }

  append(event: AgentEvent) {
    this.events.push(event);
  }

  async sendText(text: string) {
    await this.chat.sendText(text, {
      runConfig: {
        additionalSystemInstructions: buildWorkspaceInstructions(this.workspace),
        contextOverrides: {
          workspace: this.workspace
        }
      }
    });
  }
}
```

The exact implementation can change, but the shape should remain:

```text
raw events in
derived timeline out
```

## Projection Model

The projector should be a pure TypeScript function:

```ts
export function projectChatEvents(events: readonly ChatRuntimeEvent[]): ChatTimelineItem[] {
  // deterministic projection only
}
```

It should not fetch. It should not mutate the transport. It should not know about Svelte components.

The projector is responsible for:

- ordering events;
- grouping turn lifecycle events;
- coalescing text;
- coalescing reasoning;
- tracking tool calls by call ID;
- attaching tool args and tool result payloads;
- identifying pending permissions;
- identifying pending clarifications;
- preserving unknown events;
- producing stable item IDs.

The UI should not understand event ordering rules.

## Timeline Item Shape

Keep the app-owned UI model semantic and boring:

```ts
export type ChatTimelineItem =
  | UserMessageItem
  | AssistantTextItem
  | ReasoningItem
  | ToolCallItem
  | PermissionItem
  | ClarificationItem
  | BranchEventItem
  | UnknownEventItem;
```

Example:

```ts
export type ToolCallItem = {
  kind: "tool-call";
  id: string;
  callId: string;
  name: string;
  args: unknown;
  result?: ToolResultPayload;
  status: "pending" | "running" | "completed" | "failed";
  startedAt?: string;
  completedAt?: string;
};
```

This model is intentionally not a copy of HPD-Agent event types. It is the UI projection of those events.

## Component Shape

`ChatWorkspace.svelte` should stay mostly layout-oriented:

```svelte
<section class="hpd-chat-route">
  <ChatWorkspacePane {session} />
  <section class="hpd-app-slot">
    <ChatResizeHandle {chat} {mode} />
    <ChatAppPane />
  </section>
</section>
```

`ChatWorkspacePane.svelte` should compose chat UI:

```svelte
<section class="hpd-workspace-pane" aria-label="Chat">
  <ChatTimeline items={session.timeline} />
  <ChatComposer onSubmit={(text) => session.sendText(text)} />
</section>
```

`ChatTimeline.svelte` should be dumb:

```svelte
{#each items as item (item.id)}
  {#if item.kind === "assistant-text"}
    <TextCard {item} />
  {:else if item.kind === "reasoning"}
    <ReasoningCard {item} />
  {:else if item.kind === "tool-call"}
    <ToolCard {item} />
  {:else}
    <UnknownEventCard {item} />
  {/if}
{/each}
```

The cards render timeline items. They do not mutate the event log.

`ShellSidebar.svelte` should compose route navigation and the route-specific sidebar surface. For the chat route, that surface is the workspace-scoped session list:

```svelte
<ShellRouteNav {shell} />

{#if activeRoute === "chat"}
  <ChatSessionList
    sessions={chatSessions.sessions}
    activeSessionId={chatSessions.activeSessionId}
    onSelect={(sessionId) => chatSessions.select(sessionId)}
    onCreate={() => chatSessions.create()}
  />
{/if}
```

The exact components can change, but the ownership should not: the sidebar manages session selection; the chat pane renders the selected session.

## Tool Rendering

Use a small registry with a generic fallback. The useful lesson from opencode is the registry shape, not the old `Message + Part[]` model.

The tool UI should be event-native, but it should not persist every event. Durable history should contain semantic facts. Live streaming transport should stay live.

### Live Versus Durable Tool Events

The projector must accept both live `AgentEvent[]` and hydrated `BranchEvent[]`, but those streams are allowed to have different detail levels.

Live-only events:

- `EXECUTE_COMMAND_OUTPUT_CHUNK`
- `EXECUTE_COMMAND_PROGRESS`
- `EXECUTE_COMMAND_BACKGROUND_LIST`

Durable events:

- `TOOL_CALL_RESULT`
- `EXECUTE_COMMAND_PROCESS_STARTED`
- `EXECUTE_COMMAND_PROCESS_EXITED`
- `EXECUTE_COMMAND_AUTO_BACKGROUNDED`
- `FILE_EDIT_APPLIED`
- `FILE_WRITE_APPLIED`
- `LANGUAGE_SERVER_DIAGNOSTICS_RECEIVED`

This is intentional. Command output chunks are useful for live feel, but they are not the durable source of truth. Reloaded command cards should hydrate from the command start event, the final tool result, and the process exit event. File mutation events are durable because they are semantic facts about code changes, not streaming noise.

The projection invariant:

```text
live AgentEvent[] + live-only chunks/progress
durable BranchEvent[] + semantic facts/results
        -> same ToolTimelineItem shape
```

### HPD Event Advantage

HPD-OS should preserve the structure HPD-Agent gives it. Do not collapse HPD events into an opencode-style tool snapshot too early.

opencode's shell UI is effective, but its durable shape is comparatively compact:

```text
tool input + tool metadata
    command
    workdir
    description
    output
    exit
    truncated
    outputPath
```

HPD command execution exposes a fuller lifecycle:

```text
EXECUTE_COMMAND_PROCESS_STARTED
    command
    base command
    category
    working directory
    shell
    process id
    timeout
    background eligibility

EXECUTE_COMMAND_OUTPUT_CHUNK
    live stdout/stderr text
    observed byte counts
    binary/truncated/suppressed flags

EXECUTE_COMMAND_PROGRESS
    elapsed time
    stdout/stderr/combined byte counts
    discarded bytes
    output suppression state

TOOL_CALL_RESULT
    durable final visible result

EXECUTE_COMMAND_PROCESS_EXITED
    exit code
    completion kind
    duration
    stdout/stderr/combined byte counts
    discarded bytes
    truncation/drain state
    artifact paths
    content ids
    local paths

EXECUTE_COMMAND_AUTO_BACKGROUNDED
    background task id
    backgrounded timestamp
    elapsed time
```

That means the HPD command card can be better than a plain shell-output card after reload. It can explain what happened to the process, not merely display the last output text.

The same is true for file mutations. opencode's useful UI idea is "show a diff in the tool card." HPD's stronger substrate is that edits and writes are durable semantic facts:

```text
FILE_EDIT_APPLIED / FILE_WRITE_APPLIED
    path and display path
    mutation kind
    created/changed flags
    before/after snapshots
    encoding, BOM, line ending, hashes, byte lengths, line counts
    text edits and ranges
    hunks
    diff stats
    truncation/omission state
    edit replacement metadata
    normalization notes
    write mode
```

The UI should therefore project from semantic events into cards, not parse user-facing result text unless it is handling an old run, a failed call, a no-op write, or a missing mutation event.

This is the core design difference:

```text
opencode
    durable-ish tool part snapshot
    UI renders from metadata

HPD-OS
    live and durable event stream
    projector assembles semantic timeline items
    UI renders stable timeline models
```

The projector is the small price HPD-OS pays for better reload behavior, better auditability, and richer command/file cards.

```ts
export type ToolCardRenderer = ComponentType<{
  item: ToolCallItem;
}>;

export const toolCardRegistry: Record<string, ToolCardRenderer | undefined> = {
  bash: CommandCard,
  exec_command: CommandCard,
  read: FileReadCard,
  list: ContextToolCard,
  glob: SearchToolCard,
  grep: SearchToolCard,
  edit: FileMutationCard,
  write: FileMutationCard,
  apply_patch: PatchCard,
  webfetch: WebFetchCard,
  websearch: WebSearchCard,
  task: TaskCard,
  todowrite: TodoCard,
  question: QuestionCard,
  skill: SkillCard
};
```

The first version should render:

- tool name;
- status;
- args summary;
- result summary;
- expandable raw result;
- error state.

Specialized cards can be added only where they remove real complexity:

- command output;
- file edit diff;
- patch summary;
- web result;
- sub-agent/task result.

Unknown tools should still render safely.

### Tool Card Shell

All tool cards should share a boring shell:

```text
ToolCardShell
    icon
    title
    subtitle
    simple args
    status
    disclosure
    raw payload fallback
```

Specialized cards should customize the body, not reimplement the shell.

The shell owns:

- pending/running/completed/failed status display;
- disclosure state;
- keyboard accessibility;
- copy raw payload action;
- expandable raw input/result;
- error treatment;
- compact title/subtitle layout.

### Tool Families

The registry should map raw tool names into semantic card families:

| Family | Tools | UI behavior |
| --- | --- | --- |
| Context | `read`, `list`, `glob`, `grep` | Group repeated context gathering into one compact context card when adjacent. |
| Command | `bash`, `exec_command`, `ExecuteCommand` | Show command, working directory when available, live output while running, final output/result after reload, exit/error status, duration, truncation/artifact affordance, copy action. |
| File read/list | `read`, `list` | Show filename/directory, offset/limit when present, result summary. |
| Search | `glob`, `grep` | Show pattern/include/path and result summary. |
| File mutation | `edit`, `write`, `EditFile`, `WriteFile` | Show target file, path, mutation summary, diagnostics, semantic diff/content from durable file mutation events. |
| Patch | `apply_patch` | Show file count or filename, add/delete/move/modify badges, per-file diff accordions. |
| Web | `webfetch`, `websearch` | Show URL/query/provider and extracted links when useful. |
| Task/sub-agent | `task` | Show agent/subagent, description, child session link if available. |
| Todos | `todowrite` | Do not force into normal transcript; surface in a todo/status dock or compact timeline summary. |
| Questions | `question` | Pending questions should be interactive prompts; answered questions can become timeline history. |
| Skills | `skill` | Show skill name and status; details usually unnecessary. |
| Unknown | everything else | Render with `UnknownToolCard` and preserve raw input/result. |

### Context Tool Grouping

Context tools are often noisy when rendered one-by-one. Consecutive context tools should be grouped:

```text
read + list + grep + glob
    -> ContextGroupCard
        "Gathered context"
        "3 reads, 2 searches, 1 list"
```

The group can expand to show individual tool rows. This keeps the main transcript readable while preserving auditability.

The grouping rule belongs in the projector, not in Svelte components:

```ts
const contextTools = new Set(["read", "list", "glob", "grep"]);
```

If HPD tools use different names, normalize them into semantic families before rendering.

### Command Card Projection

Command cards should be built from multiple event sources:

```text
EXECUTE_COMMAND_PROCESS_STARTED
    -> create/update command item with command, cwd, shell, timeout, background flag

EXECUTE_COMMAND_OUTPUT_CHUNK
    -> append live stdout/stderr text while the run is active

EXECUTE_COMMAND_PROGRESS
    -> update live byte counts / subtle running status only

TOOL_CALL_RESULT
    -> durable final visible output fallback

EXECUTE_COMMAND_PROCESS_EXITED
    -> final exit code, completion kind, duration, truncation, artifact/content refs
```

On reload, there may be no output chunks. That is expected. The projector should still create a complete command card from:

```text
PROCESS_STARTED + TOOL_CALL_RESULT + PROCESS_EXITED
```

The command card should render:

- command text;
- working directory when useful;
- shell/base command/category when useful;
- live output while running;
- final output from `TOOL_CALL_RESULT` when hydrated;
- exit code and completion kind;
- duration;
- stdout/stderr/combined byte counts in details;
- truncation state;
- artifact/content links from `PROCESS_EXITED` when output was truncated, suppressed, binary, or too large.

Do not reconstruct durable command output by replaying persisted output chunks. Chunks are live UI transport. The final tool result plus process exit event are the durable command state.

This is the second-mover version of opencode's shell output model: keep the compact shell card, collapse long output, and provide an output file/artifact affordance, but use HPD's stronger event model instead of storing a single tool-part snapshot.

### Terminal Output Rendering

Do not render chat command cards with a full terminal emulator.

opencode uses PTY infrastructure to execute commands (`bun-pty` / `@lydell/node-pty` depending on runtime), but the chat/timeline display is intentionally simpler:

- strip ANSI from shell output;
- normalize line endings;
- render command plus output as text inside a contained preformatted region;
- collapse long output;
- show copy affordances;
- show final exit/truncation/artifact state separately.

HPD-OS should follow that boundary.

The command card is not an interactive terminal session. It is a replayable audit card for a command execution. Rendering it with xterm-style terminal state would add cursor-screen semantics that do not match durable branch hydration, especially after reload.

Recommended first implementation:

```text
CommandCard
    header: command / cwd / status / exit
    body:
        LiveCommandOutputBuffer while running
        FinalCommandOutput from TOOL_CALL_RESULT after completion or reload
    details:
        process id
        shell
        duration
        byte counts
        truncation
        artifact/content refs
```

ANSI handling should be conservative. Strip ANSI in the first version. Add ANSI-to-span rendering only if colored test output or compiler diagnostics become materially easier to scan with color. Do not introduce a terminal emulator for chat cards unless HPD-OS later adds a separate interactive terminal app surface.

### File Mutation Card Projection

Edit and write cards should prefer semantic coding events over tool result text.

Use `FILE_EDIT_APPLIED` for `EditFile` / `edit` cards:

- `Path` and `DisplayPath`;
- `MutationKind`;
- `Created` and `Changed`;
- `Before` and `After` snapshots;
- `TextEdits`;
- `Hunks`;
- `HunksTruncated`;
- `DiffStat`;
- `Notes`;
- `EditCount`;
- `ReplacementCount`;
- `Replacements`;
- `Normalizations`.

Use `FILE_WRITE_APPLIED` for `WriteFile` / `write` cards:

- `Path` and `DisplayPath`;
- `Mode`;
- `MutationKind`;
- `Created` and `Changed`;
- `Before` and `After` snapshots;
- `TextEdits`;
- `Hunks`;
- `HunksTruncated`;
- `DiffStat`;
- `Notes`.

The result XML is not the primary diff source. It is a fallback summary for old runs, failed calls, no-op writes, or missing mutation events.

Diagnostics should be correlated by file path and tool timing where possible. `LANGUAGE_SERVER_DIAGNOSTICS_RECEIVED` provides durable diagnostic lifecycle facts; richer diagnostic details may also appear in tool result text or future diagnostic payloads. Cards should have a diagnostics slot now, even if the first version only shows summary counts.

### Diff Rendering

The useful lesson from opencode is the user experience:

- show a compact `+N -N` summary in the tool card header;
- expand into a readable file diff;
- keep long or complex diffs contained inside the card;
- allow a raw payload fallback.

Do not copy opencode's diff storage model. HPD already emits semantic mutation events with enough data to render diffs directly.

Map HPD file mutation data into the UI like this:

```text
FILE_EDIT_APPLIED / FILE_WRITE_APPLIED
    DiffStat.AddedLines / DiffStat.RemovedLines
        -> DiffStatBadge

    Hunks[]
        -> DiffHunkView

    Before.Text + After.Text
        -> optional richer before/after diff source

    HunksTruncated / TextOmitted / OmissionReason
        -> truncation or omitted-content notice
```

The first implementation should use a native Svelte hunk renderer rather than introducing a heavyweight diff viewer:

```text
DiffHunkView
    header: @@ -oldStart,oldLines +newStart,newLines @@
    lines:
        "+" -> added line styling
        "-" -> removed line styling
        " " -> context line styling
```

This is enough for the chat timeline because the backend already computes line hunks and diff stats. A full diff engine can be added later only if we need split diffs, inline word diffs, virtualization, line selection, comment anchors, or search inside large diffs.

Diff card behavior:

- `EditFile` should title as `Edit {filename}` and show replacement count when available.
- `WriteFile` should title as `Create {filename}`, `Write {filename}`, or `Rewrite {filename}` based on `Mode` and `Created`.
- The header should show `+AddedLines -RemovedLines` when `DiffStat` is nonzero.
- The body should prefer `Hunks`.
- If `Hunks` are unavailable but `Before.Text` and `After.Text` are present, the UI may compute a client-side diff.
- If text is omitted or hunks are truncated, show the omission/truncation reason and keep the raw event expandable.
- No-op writes should render from the tool result summary because no mutation event is emitted.

### Fallback Requirement

Unknown tools must never disappear.

The fallback should show:

- tool name;
- status;
- first useful label from args, such as `description`, `query`, `url`, `filePath`, `path`, `pattern`, or `name`;
- up to three primitive args;
- expandable raw input/result.

This makes MCP/custom tools safe by default.

## Composer Model

Start with a text composer, but give it a shape that can grow:

```ts
export type ComposerDraft = {
  text: string;
  files: FileReference[];
  assets: AssetReference[];
  selectedRanges: FileRange[];
  runConfig: ChatRunConfig;
};
```

The first implementation can send only text:

```ts
await chat.sendText(draft.text, {
  runConfig: mergeRunConfig(draft.runConfig, workspaceRunConfig)
});
```

Future additions should not require changing the timeline architecture:

- file references;
- selected ranges;
- uploaded assets;
- image input;
- agent/model selector;
- permission defaults;
- slash commands;
- queued followups;
- shell mode.

## Branching Model

Do not copy opencode's "fork equals child session" model.

HPD has first-class branches inside sessions. HPD-OS should use them directly:

- session = conversation container;
- branch = timeline variant;
- sibling branches = alternate futures at a fork point;
- branch events = durable timeline source;
- branch comparison/evals = future analysis surface.

The branch UI should eventually support:

- active branch selector;
- previous/next sibling;
- fork from message;
- branch rename/tags;
- delete branch;
- compare branches.

The projector should treat branch metadata events as optional timeline/debug items, not as normal assistant prose.

## Main Chat Versus Debug Timeline

Not every event belongs in the main chat.

The main timeline should show:

- user text/input;
- assistant text;
- reasoning summaries;
- tool cards;
- coding cards;
- permission prompts;
- clarification prompts;
- durable branch/fork milestones when useful.

The debug timeline should show:

- raw unknown events;
- middleware state changes;
- provider lifecycle events;
- low-level stream lifecycle;
- diagnostic events;
- exact event metadata.

The projector can produce both:

```text
AgentEvent[] / BranchEvent[]
    -> ChatTimelineItem[]
    -> DebugTimelineItem[]
```

## Styling Strategy

Use Tailwind CSS v4 for local component layout:

```svelte
<article class="rounded-md border px-3 py-2" data-kind={item.kind}>
```

Use scoped chat CSS only for shared card/timeline selectors that Tailwind would make noisy:

```css
.hpd-chat-card[data-status="running"] {
  /* shared running state */
}
```

Do not put app runtime styling into the global shell CSS.

Global `styles.css` should own only:

- Tailwind import;
- font faces;
- theme tokens;
- base document rules;
- small shared utilities used across routes.

Chat-specific layout and runtime styling belongs in `svelte/chat/styles.css`.

That includes:

- chat route layout;
- workspace pane and app pane layout;
- resize handle styling;
- timeline layout;
- message cards;
- reasoning cards;
- tool cards;
- diff/code cards;
- composer styling;
- chat-specific container queries.

The existing old root-level chat selectors should not be ported wholesale. Selectors such as `hpd-conversation-*`, `hpd-chat-stack`, `hpd-message`, `hpd-tool`, `hpd-composer`, and `hpd-artifact` came from the previous all-in-one stylesheet era. If no current component references them, delete them from root `styles.css` and recreate only the styles required by the new event-native chat components.

The CSS ownership rule:

```text
styles.css
    -> app tokens, base rules, shared utilities

svelte/shell/styles.css
    -> shell frame, sidebar, route chrome

svelte/chat/styles.css
    -> chat layout, chat timeline, chat cards, chat composer
```

## Accessibility

The timeline and composer should be accessible from the first implementation.

Requirements:

- main transcript uses a named region;
- composer has a real label;
- submit button is a button;
- streaming status uses polite live regions where useful;
- tool cards expose expanded/collapsed state;
- permission and clarification prompts are keyboard reachable;
- error cards are announced with useful text;
- debug timeline is not forced into the main reading order.

## Implementation Plan

### Phase 1: Runtime Spine

- Add `runtime/agentClient.ts`.
- Add `runtime/workspaceContext.ts`.
- Add `runtime/chatSessions.svelte.ts`.
- Add `runtime/chatTypes.ts`.
- Add `runtime/chatProjector.ts`.
- Add `runtime/chatSession.svelte.ts`.
- Add tests for projector behavior.

Minimum runtime/projection tests:

- hydrated branch text becomes an assistant text item;
- live text deltas coalesce into the same final assistant text item;
- tool start/args/result/end becomes one tool item;
- unknown events are preserved;
- branch metadata events do not become assistant prose;
- workspace metadata is included when creating/searching sessions;
- workspace context is included in `runConfig.contextOverrides.workspace`.

### Phase 2: Workspace-Scoped Session List

- Add sidebar session list for the chat route.
- Load active HPD-OS workspace.
- Search sessions by workspace metadata.
- Create sessions with workspace metadata.
- Persist active session id separately from workspace ownership.
- Pass `sessionId` and `branchId` into the chat route.

### Phase 3: Minimal UI

- Add `ChatTimeline.svelte`.
- Add `ChatComposer.svelte`.
- Replace `ChatWorkspacePane.svelte` blank surface with timeline + composer.
- Keep `ChatWorkspace.svelte` layout-oriented.
- Move new timeline/composer/card styling into `svelte/chat/styles.css`.
- Delete unused root-level chat selectors instead of porting stale CSS forward.

### Phase 4: Workspace-Aware Runs

- Build `HpdosRunWorkspace` from the active workspace roots.
- Add workspace context to every run through `contextOverrides.workspace`.
- Add concise workspace instructions through `additionalSystemInstructions`.
- Verify coding tools reject paths outside configured roots.

### Phase 5: Tool Cards

- Add generic `ToolCard`.
- Add `CommandCard`.
- Add `FileMutationCard`.
- Add `DiffStatBadge`.
- Add `DiffHunkView`.
- Render `FILE_EDIT_APPLIED` and `FILE_WRITE_APPLIED` from durable mutation events.
- Render no-op writes and missing-event mutations from tool result fallback.
- Add raw result expansion.
- Keep unknown tools renderable.

### Phase 6: Branch Controls

- Load branches for session.
- Switch active branch.
- Hydrate switched branch events.
- Fork from a message.
- Add sibling navigation.

### Phase 7: Rich Composer

- Add file references.
- Add asset uploads.
- Add selected ranges.
- Add agent/model/run config controls.
- Add queued followups only if needed.

## Success Criteria

This architecture is successful when:

- live rendering and reload hydration converge on the same timeline;
- Svelte components render semantic timeline items, not raw protocol noise;
- chat runtime code is separate from pane/layout code;
- HPD-Agent API access goes through `@hpd/hpd-agent-client`;
- sessions are searched and created through workspace metadata;
- the sidebar owns session selection;
- the chat pane renders only the selected session branch;
- every run includes workspace context for the coding harness;
- model-visible workspace instructions and tool-enforced workspace context stay separate;
- tool rendering is extensible without changing the projector;
- unknown events are not lost;
- branches are first-class in the UI;
- the first useful UI requires less code than an opencode-style part store.

## Recommendation

Proceed with an event-native HPD-OS chat.

The right foundation is:

```text
HPD-Agent TypeScript client
    -> raw typed API and events

HPD-OS chat runtime
    -> hydrate, stream, project

HPD-OS timeline components
    -> render semantic cards
```

This gives HPD-OS the same visible power as opencode's chat surface while avoiding its historical compatibility layers.
