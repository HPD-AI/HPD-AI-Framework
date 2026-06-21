# Thread Timeline Projection

## Summary

The headless UI model is timeline-first. A thread is not a flat chat log with a
few side arrays; it is a structured lifecycle projection:

```ts
timeline: ThreadTimelineItem[];
workGroups: ThreadWorkGroup[];
transcriptMessages: Message[];
activeTools: ToolCall[];
pendingRuntimeRequests: RuntimeRequest[];
activity: ThreadActivity;
```

This is the public contract. There is no compatibility layer for the older
flat-message prototype.

The projection represents the lifecycle shape agent UI actually needs:

- work begins when a turn/run begins
- reasoning, draft assistant text, tool calls, hooks, and progress happen inside
  that work
- related tool calls may need grouped summaries
- work collapses after completion
- final assistant output is promoted into the transcript
- failed/cancelled work remains inspectable without pretending to be a normal
  answer

Messages remain important, but they become leaf content inside a richer
timeline/work projection. Svelte, React, or any other adapter should render this
projection. They should not reconstruct lifecycle by handling raw protocol
events or mutating DOM.

## Why Timeline-First

A flat message list loses durable lifecycle meaning:

- `currentTurnId` and `currentConversationId` are transient and cleared when the
  turn finishes.
- `Message` does not know which turn, conversation, or run produced it.
- `ToolCall` does not know which turn, conversation, or run produced it.
- completed tools are removed from `activeTools`.
- a message-list component can only render messages, so it cannot place work
  groups, nested tool groups, or runtime prompts into the transcript.

This creates pressure to rebuild the archived pattern: render events directly
into DOM, move nodes around on lifecycle events, and collapse groups imperatively.
That is exactly what the headless architecture should avoid.

Lifecycle meaning belongs in the core projection.

## Reference Lessons

### Archived HPD UI

The archive discovered a real UX need:

- open a working group during a turn
- put reasoning/tool activity inside it
- group exploration-style tool calls
- collapse completed work
- promote final assistant text outside the work group

But it solved that need in the wrong layer by mixing protocol event handling,
state folding, DOM placement, and visual behavior.

### VS Code Copilot Chat

Copilot Chat is evidence that mature agent UIs need structured response parts,
not just message text. It has distinct concepts for:

- markdown response parts
- progress
- thinking progress
- tool invocation begin/update
- confirmations
- edits
- warnings
- hook progress
- timeline mutation such as clearing to a previous tool invocation

We should not copy its VS Code-specific API shape. We should extract the
second-mover lesson: the transcript is a structured timeline.

## Design Goals

- Make thread UI timeline-first.
- Keep protocol/event parsing out of framework adapters.
- Give apps strong rendering control without requiring raw event handling.
- Make work/tool lifecycle inspectable after completion.
- Support grouped activity without making grouping a DOM trick.
- Preserve simple message leaf renderers without preserving a flat transcript
  contract.
- Keep controllers scoped to one thread.
- Do not recreate Workspace or a global active thread runtime.

## Non-Goals

- Do not define final visual styling.
- Do not make Svelte the owner of lifecycle meaning.
- Do not put session/thread navigation into `ThreadController`.
- Do not force one grouping policy on every app.
- Do not preserve the prototype `messages` snapshot API.
- Do not ship a compatibility adapter for the archive or the first Svelte slice.

## Proposed Core Types

The projection snapshot is:

```ts
export interface ThreadProjectionSnapshot {
  thread: Thread | null;
  timeline: ThreadTimelineItem[];
  workGroups: ThreadWorkGroup[];
  transcriptMessages: Message[];
  activeTools: ToolCall[];
  pendingRuntimeRequests: RuntimeRequest[];
  threadRun: ThreadRunView | null;
  activity: ThreadActivity;
  currentTurnId: string | null;
  currentConversationId: string | null;
  currentRunId: string | null;
  error: string | null;
  canSend: boolean;
}

export interface ThreadActivity {
  status: 'idle' | 'working' | 'requesting' | 'failed' | 'cancelled';
  streaming: boolean;
  reasoning: boolean;
  activeToolCount: number;
  pendingRequestCount: number;
}
```

```ts
export type ThreadTimelineItem =
  | ThreadTimelineMessageItem
  | ThreadTimelineWorkItem
  | ThreadTimelineRuntimeRequestItem
  | ThreadTimelineProgressItem
  | ThreadTimelineWarningItem;

export interface ThreadTimelineMessageItem {
  type: 'message';
  id: string;
  message: Message;
  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
  eventFlowId?: string;
  sequenceNumber?: number;
}

export interface ThreadTimelineWorkItem {
  type: 'work';
  id: string;
  work: ThreadWorkGroup;
  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
}

export interface ThreadTimelineRuntimeRequestItem {
  type: 'runtime-request';
  id: string;
  request: RuntimeRequest;
  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
}

export interface ThreadTimelineProgressItem {
  type: 'progress';
  id: string;
  label: string;
  event?: AgentEvent;
}

export interface ThreadTimelineWarningItem {
  type: 'warning';
  id: string;
  message: string;
  event?: AgentEvent;
}
```

Work groups:

```ts
export type ThreadWorkStatus =
  | 'working'
  | 'worked'
  | 'failed'
  | 'cancelled';

export interface ThreadWorkGroup {
  id: string;
  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
  status: ThreadWorkStatus;
  label: string;
  openByDefault: boolean;
  parts: ThreadWorkPart[];
  finalMessageId?: string;
  startedAt?: string;
  completedAt?: string | null;
  error?: string | null;
}

export type ThreadWorkPart =
  | ThreadWorkReasoningPart
  | ThreadWorkAssistantDraftPart
  | ThreadWorkToolPart
  | ThreadWorkToolGroupPart
  | ThreadWorkProgressPart
  | ThreadWorkHookPart
  | ThreadWorkWarningPart;

export interface ThreadWorkReasoningPart {
  type: 'reasoning';
  id: string;
  messageId: string;
  text: string;
  status: 'streaming' | 'complete';
  eventFlowId?: string;
  sequenceNumber?: number;
}

export interface ThreadWorkAssistantDraftPart {
  type: 'assistant-draft';
  id: string;
  message: Message;
}

export interface ThreadWorkToolPart {
  type: 'tool';
  id: string;
  tool: ToolCall;
}

export interface ThreadWorkToolGroupPart {
  type: 'tool-group';
  id: string;
  group: ThreadToolGroup;
}

export interface ThreadWorkProgressPart {
  type: 'progress';
  id: string;
  label: string;
  event?: AgentEvent;
}

export interface ThreadWorkHookPart {
  type: 'hook';
  id: string;
  label: string;
  event?: AgentEvent;
}

export interface ThreadWorkWarningPart {
  type: 'warning';
  id: string;
  message: string;
  event?: AgentEvent;
}
```

Tool groups:

```ts
export interface ThreadToolGroup {
  id: string;
  label: string;
  summary: string;
  status: 'active' | 'complete' | 'error';
  tools: ToolCall[];
  openByDefault: boolean;
}
```

## Message And Tool Metadata

Break `Message` and `ToolCall` so lifecycle identity is preserved after a turn
finishes.

```ts
export type MessagePlacement =
  | 'transcript'
  | 'work'
  | 'final';

export interface Message {
  id: string;
  role: MessageRole;
  content: string;
  streaming: boolean;
  thinking: boolean;
  timestamp: Date;
  toolCalls: ToolCall[];
  reasoning?: string;
  authorName?: string;

  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
  eventFlowId?: string;
  sequenceNumber?: number;
  placement: MessagePlacement;
}

export interface ToolCall {
  callId: string;
  name: string;
  messageId: string;
  status: ToolCallStatus;
  startTime: Date;
  endTime?: Date;
  args?: unknown;
  result?: ToolResultPayload;
  resultText?: string;
  error?: string;
  toolharnessName?: string;
  callType?: ToolCallType;

  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
  eventFlowId?: string;
  sequenceNumber?: number;
  groupKey?: string;
}
```

## Projection Behavior

### Turn Start

On `MESSAGE_TURN_STARTED`:

- set `currentTurnId`
- set `currentConversationId`
- keep `currentRunId` if one is active
- create or activate a current `ThreadWorkGroup`
- mark it `working`
- set `openByDefault = true`
- add a work item to `timeline`

### Run Start

On `THREAD_RUN_STARTED`:

- set `threadRun.status = active`
- store current `runId`
- attach `runId` to subsequent messages/tools/work groups when available

### Reasoning

Reasoning should be modeled as work, not as normal transcript text.

- reasoning start creates/updates a work part
- reasoning deltas append to that part
- reasoning end marks it complete

The app decides whether reasoning is visible, collapsed, or hidden.

### Assistant Draft Text

Assistant text that streams during an active work group should be modeled as an
assistant draft/work part until lifecycle says it is final.

On completion, the projection promotes the final assistant message into
`transcriptMessages` and the timeline while keeping the work group collapsed
above it.

### Tools

Tool calls should be retained in durable work state even after they leave
`activeTools`.

- `activeTools` remains useful as a live status helper.
- timeline/work groups retain completed tools.
- related tools may be grouped by selector policy.

### Runtime Requests

Runtime requests remain a unified queue. Timeline placement should be explicit:

- blocking user/client prompts may render as timeline items
- product UIs may choose to render them inline, in a side panel, or as modals

Core should expose request identity and placement. It should not impose modal
policy.

### Turn Finish

On `MESSAGE_TURN_FINISHED`:

- mark current work group `worked`
- set `openByDefault = false`
- clear current turn state
- promote final assistant message into `transcriptMessages` when known

### Run Completion/Error/Cancel

On `THREAD_RUN_COMPLETED`:

- if cancelled, mark current work group `cancelled`
- if error, mark current work group `failed`
- otherwise mark current work group `worked`
- preserve work parts for inspection

## User Control Model

The core owns meaning. The app owns presentation.

Core decides:

- this item is work
- this item is a final message
- this tool belongs to this turn
- this work group completed
- this request is pending

App decides:

- show completed work collapsed or expanded
- render requests inline, sidebar, or modal
- show tool arguments inline or behind details
- render custom work parts

Selectors accept policy options that change timeline membership:

```ts
export interface ThreadTimelineOptions {
  completedWork?: 'collapsed' | 'expanded' | 'hidden';
  runtimeRequests?: 'inline' | 'exclude';
}
```

Reasoning display, tool grouping, tool arguments, and custom work parts remain
work-part rendering policy. Use `ThreadWorkGroup`, `ThreadWorkParts`, and
snippets to change that presentation.

Svelte can expose the implemented selector controls at component level:

```svelte
<ThreadTimeline
  {thread}
  completedWork="collapsed"
  runtimeRequests="inline"
/>
```

And full snippet control:

```svelte
<ThreadTimeline {thread}>
  {#snippet message(ctx)}...{/snippet}
  {#snippet work(ctx)}...{/snippet}
  {#snippet tool(ctx)}...{/snippet}
  {#snippet request(ctx)}...{/snippet}
</ThreadTimeline>
```

## New Selectors

Add pure selectors in `hpd-agent-headless-ui`:

```ts
getThreadTimeline(snapshot, options?)
getThreadWorkGroups(snapshot, options?)
getTranscriptMessages(snapshot)
getThreadToolGroups(snapshot, options?)
```

There is no `getVisibleMessages(snapshot)` selector in the timeline-first
contract. The transcript selector is `getTranscriptMessages(snapshot)`.

## Svelte Adapter Impact

`ThreadStateSnapshot` mirrors the core shape:

```ts
interface ThreadStateSnapshot {
  projection: ThreadProjectionSnapshot;
  timeline: ThreadTimelineItem[];
  workGroups: ThreadWorkGroup[];
  transcriptMessages: Message[];
  activeTools: ToolCall[];
  pendingRuntimeRequests: RuntimeRequest[];
  activity: ThreadActivity;
  ...
}
```

Keep:

- `ThreadComposer`
- `ThreadStatus`
- `RuntimeRequest`
- `ThreadRuntimeRequests`
- `Message` as a leaf renderer

Remove/rebuild:

- `ThreadMessages` is not part of the primary contract. If it comes back, it is
  a leaf/helper built on `transcriptMessages`, not a transcript architecture.

Add:

- `ThreadTimeline`
- `ThreadWorkGroup`
- `ThreadToolActivity` or tool render snippets inside timeline

## Break Direction

This package is early enough to break completely. There is no backwards
compatibility promise for the first Svelte slice.

Break order:

1. Replace the projection snapshot with timeline/work/transcript fields.
2. Stamp turn/conversation/run metadata onto `Message` and `ToolCall`.
3. Preserve completed tools inside work groups.
4. Add timeline/work selectors.
5. Update projection tests around turn lifecycle.
6. Update Svelte `ThreadStateSnapshot`.
7. Rewrite Svelte docs around timeline-first composition.
8. Build `ThreadTimeline`.
9. Rebuild Storybook around timeline-first DX.

## First Implementation Slice

The smallest useful code slice should be core-only:

- extend `Message` and `ToolCall` with lifecycle metadata
- add `ThreadWorkGroup` and `ThreadTimelineItem` types
- replace `messages` with `transcriptMessages`
- add `timeline`, `workGroups`, `activity`, and `currentRunId` to
  `ThreadProjectionSnapshot`
- update projection to create/finalize one work group around
  `MESSAGE_TURN_STARTED` / `MESSAGE_TURN_FINISHED`
- add tests for:
  - messages created during a turn receive `turnId`
  - tool calls created during a turn receive `turnId`
  - completed tools remain inspectable in the work group
  - final assistant message is transcript-visible after turn completion
  - failed/cancelled work group status is preserved

No Svelte component should be added until this model is stable.

## Principle

Do not make components rediscover lifecycle from events.

Do not make app developers choose between raw protocol handling and weak flat
messages.

Expose a strong headless timeline read model, then let apps render it with as
much or as little control as they want.
