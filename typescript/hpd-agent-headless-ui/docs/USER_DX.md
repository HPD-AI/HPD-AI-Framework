# User DX Guide

This package is the framework-neutral thread UI core for HPD Agent. It owns the
UI projection of one thread: durable transcript leaves, live work lifecycle,
pending runtime requests, activity state, and selectors. It does not own Svelte,
React, DOM, protocol message reconstruction, or app-level session routing.

The core absorbs protocol and lifecycle awkwardness so app authors do not need
to know every event edge case. Live streaming and durable hydration are folded
through the same projection model. Reasoning stays in work groups, final
assistant output becomes transcript leaves, tool calls stay attached to turn
work, and runtime requests stay pending until lifecycle events resolve them.
Apps choose how those projected parts look.

## Mental Model

```text
Session = shared container
Thread = durable event stream / runtime scope
Transcript = final user-visible message leaves
Work = live or completed turn activity
Timeline = ordered transcript leaves and work groups
Runtime request = request/response envelope awaiting user or client action
Revision = fork a thread path and resend user input
```

The controller rule is intentionally small: one controller represents one thread
scope.

```ts
{
  agentId: string;
  sessionId: string;
  threadId: string;
}
```

Route events into the controller they belong to. Do not use a controller as a
global active-thread runtime. Subagent threads are ordinary child thread scopes;
inspect them by creating or selecting a controller for that child thread.

## Happy Path

Use `createThreadController` when building a chat surface or framework adapter.

```ts
import { AgentClient } from '@hpd-research/hpd-agent-client';
import {
  canSubmitText,
  createThreadController,
  getPendingRuntimeRequests,
  getThreadTimeline,
  getThreadWorkGroups,
  getTranscriptMessages,
} from '@hpd-research/hpd-agent-headless-ui';

const client = new AgentClient({
  baseUrl: 'http://localhost:5000',
  transport: 'sse',
});

const thread = createThreadController({
  client,
  agentId: 'agent-1',
  sessionId: 'session-1',
  threadId: 'thread-1',
});

const unsubscribe = thread.projection.subscribe((snapshot) => {
  render({
    timeline: getThreadTimeline(snapshot),
    workGroups: getThreadWorkGroups(snapshot),
    transcriptMessages: getTranscriptMessages(snapshot),
    activity: snapshot.activity,
    activeTools: snapshot.activeTools,
    pendingRuntimeRequests: getPendingRuntimeRequests(snapshot),
    canSubmitText: canSubmitText(snapshot),
    error: snapshot.error,
  });
});

await thread.start({ includeRuns: true });
await thread.sendMessage({ contents: [{ $type: 'text', text: 'Hello' }] });

unsubscribe();
await thread.dispose();
```

Use `createThreadRevisionController` when implementing edit/retry UI. Revisions
do not mutate an existing message; they fork the thread path and send user input
to the new thread.

```ts
const revisions = createThreadRevisionController({
  client,
  agentId: 'agent-1',
  sessionId: 'session-1',
  threadId: 'thread-1',
});

await revisions.forkAndRetryMessage('assistant-message-id');
await revisions.forkAndEditMessage('user-message-id', 'Replacement prompt');
```

Retry resends the same resolved user message and can target a user or assistant
message. Edit sends replacement text and is intentionally user-message only.
Both operations use the client `forkThread` and `run` primitives.

`start()` is explicit composition:

```text
load durable thread snapshot
  -> rehydrate projection
  -> connect scoped live stream
  -> project matching live events
```

## Snapshot Shape

The projection snapshot is timeline-first:

```ts
interface ThreadProjectionSnapshot {
  timeline: ThreadTimelineItem[];
  workGroups: ThreadWorkGroup[];
  transcriptMessages: Message[];
  activeTools: ToolCall[];
  pendingRuntimeRequests: RuntimeRequest[];
  threadRun?: ThreadRun;
  activity: ThreadActivity;
  currentTurnId?: string;
  currentConversationId?: string;
  currentRunId?: string;
  error?: Error;
  canSend: boolean;
}
```

There is no compatibility `messages` field and no
`getVisibleMessages(snapshot)` selector. Durable protocol messages are still
loaded from the client, but the UI-facing output is `transcriptMessages` plus
`timeline`.

## Timeline

Use the timeline when you want a real conversation UI. It can contain transcript
messages, live work groups, completed collapsed work groups, runtime requests,
and future custom items.

```ts
for (const item of getThreadTimeline(snapshot)) {
  if (item.kind === 'message') renderMessage(item.message);
  if (item.kind === 'work') renderWorkGroup(item.workGroup);
  if (item.kind === 'runtime-request') renderRequest(item.request);
}
```

The user of the headless layer gets control over grouping. They can render every
tool call separately, collapse a completed turn into one row, group all work for
a turn under one disclosure, or ignore work groups and show only transcript
leaves. The projection provides structure; the renderer chooses density.

Headless owns the semantic shape:

- which events belong to transcript messages
- which events belong to work groups
- when work is `working`, `worked`, `failed`, or `cancelled`
- when assistant draft text becomes the final assistant transcript message
- when reasoning remains inspectable work instead of normal message text
- how live streaming and rehydrated events produce the same UI model

Apps own the visual policy:

- whether completed work is shown, collapsed, expanded, or hidden
- how reasoning, tool calls, and progress rows are styled
- where branch/fork controls are placed
- what happens visually after edit or retry creates a new thread

## Work Groups

`ThreadWorkGroup` is the lifecycle model for a turn. It is the answer to the
problem that old message lists could not represent: a turn is not just text. It
can contain reasoning, assistant draft text, tool calls, tool results, runtime
requests, status changes, and final transcript output.

Typical flow:

```text
turn started
  -> reasoning part appears
  -> assistant draft streams
  -> tool call starts
  -> tool call completes
  -> runtime request appears or resolves
  -> turn finishes
  -> work group collapses
  -> final assistant text is promoted to transcriptMessages
```

This is why the projection distinguishes `workGroups` from
`transcriptMessages`. A renderer can show live work while it is useful, then
collapse it when the final transcript leaf is available.

Do not infer work groups from message ids in the app. Some providers use the
same message id for reasoning and final text. The projection handles that and
keeps reasoning as work while promoting only final answer text to the
transcript.

## Transcript Messages

Use `transcriptMessages` for final chat leaves only.

```ts
for (const message of getTranscriptMessages(snapshot)) {
  renderMessage(message);
}
```

This is intentionally less expressive than the timeline. It is useful for simple
transcript views, export surfaces, tests, and adapters that want to build their
own grouping model.

## Runtime Requests

The projection exposes pending request envelopes:

```ts
snapshot.pendingRuntimeRequests
```

Each item has standardized request/response metadata:

```ts
request.id
request.kind
request.sourceName
request.requestEventType
request.expectedResponseEventType
request.responsePolicy
request.target
request.visibility
```

Known request kinds carry typed payloads. Custom request events stay visible as
custom envelopes instead of being discarded.

```ts
import { getPendingRuntimeRequests } from '@hpd-research/hpd-agent-headless-ui';

for (const item of getPendingRuntimeRequests(snapshot)) {
  if (item.kind === 'permission') renderPermission(item.request);
  if (item.kind === 'clarification') renderClarification(item.request);
  if (item.kind === 'client-tool') renderClientToolRequest(item.request);
  if (item.kind === 'custom') renderCustomRequest(item);
}
```

The controller provides response helpers for common request kinds:

```ts
await thread.approve(permissionId);
await thread.deny(permissionId, 'Not allowed');
await thread.clarify(requestId, 'Use the production tenant');
await thread.answerClientToolRequest(requestId, 'Selected screenshot.png');
```

For custom response events, use the generic response path:

```ts
await thread.respond({
  type: 'CUSTOM_RESPONSE_EVENT',
  requestId,
  sourceName,
  value: 'done',
});
```

Pending request UI should clear from lifecycle events such as resolved, expired,
or cancelled. Raw response payloads are not the generic cleanup contract.

## Sending Messages

`sendMessage()` sends one user message. Text and uploaded files travel through
the same MEAI `AIContent[]` path.

```ts
await thread.sendMessage({
  contents: [{ $type: 'text', text: 'Summarize this thread' }],
});
```

Use `run()` for lower-level protocol input.

```ts
await thread.run({
  type: 'USER_MESSAGES_INPUT',
  messages: [{
    role: 'user',
    contents: [{ $type: 'text', text: 'Hello' }],
  }],
});
```

The controller stamps missing `agentId`, `sessionId`, and `threadId` onto input
events. File upload state, content-reference formatting, and run configuration
belong in adapters above this core unless the lower protocol accepts them as
input.

For composer disabled state, use the selector:

```ts
import { getTextSubmissionState } from '@hpd-research/hpd-agent-headless-ui';

const input = getTextSubmissionState(thread.projection.getSnapshot());

renderComposer({
  disabled: !input.canSubmit,
  blockedReason: input.reason,
});
```

`snapshot.canSend` is the raw projection flag. `getTextSubmissionState()` is the
composer-facing read model and also considers activity, active tools, and
blocking runtime requests.

`input.reason === 'runtime-request'` means the user should answer a pending
request before sending more text. Runtime request response helpers such as
`approve()`, `deny()`, `clarify()`, and `answerClientToolRequest()` still use the
lower-level response path and remain valid while normal text submission is
blocked.

## API Layers

`createThreadController()` owns one thread lifecycle: load, connect, project,
send input, answer runtime requests, interrupt, disconnect, dispose.

`createThreadProjection()` is the state fold. It does not fetch, connect, or
submit. Use it when manually feeding snapshots and events.

`loadThreadSnapshot()` is the durable loader. It calls the client REST APIs,
loads durable thread events, and returns a plain snapshot. Headless replays
those events through the same projection fold used for live streaming. The
client still owns protocol/read-model helpers such as thread-message
reconstruction.

`createThreadBranchNavigator()` loads the session thread graph and derives
fork-group plus runtime-child metadata. It does not connect live streams or
project events.

`eventBelongsToScope()` is the guard for thread-safe event routing.

Selectors are pure read-model helpers. They do not subscribe, fetch, mutate,
connect, or submit.

## Thread Branch Navigation

Use `createThreadBranchNavigator` for fork-group and runtime-child UI. Keep it
separate from `ThreadController`.

```ts
import {
  createThreadBranchNavigator,
  getBranchChoiceLabel,
  getSubAgentRuntimeChildren,
  getVisibleRuntimeChildren,
  hasSubAgentRuntimeChildren,
} from '@hpd-research/hpd-agent-headless-ui';

const navigator = createThreadBranchNavigator({
  client,
  sessionId,
  threadId,
});

const nav = await navigator.load();

renderThreadSwitcher({
  current: nav.current,
  forkGroups: nav.forkGroups,
  activePathChoices: nav.activePathChoices,
  labels: nav.activePathChoices.map(getBranchChoiceLabel),
});

renderChildThreads({
  visibleChildren: getVisibleRuntimeChildren(nav),
  subAgentChildren: getSubAgentRuntimeChildren(nav),
  hasSubAgentRuntimeChildren: hasSubAgentRuntimeChildren(nav),
});
```

Branch navigator state is durable graph metadata, not a live runtime object.
Fork groups are user-visible conversation branches. Runtime children are
subagent/tool-owned threads attached to a parent thread; render them as activity,
inspectable work, or a side panel, not as ordinary fork choices.

Fork groups are semantic choice points, not direct parent-child edges. A fork
still has exact lineage through `ForkedFrom`, but the graph groups branches by
the canonical shared context: root forks belong to the same root group, and
forks after a copied message id belong to the same message-boundary group even
when the user created them from different descendant branches.

That grouping is defined in the lower C# session layer by
`ThreadForkGraph.BuildVisibleForkGroups(...)`. The TypeScript headless layer
does not invent sibling semantics; it consumes the graph exposed by the client
and turns it into UI selectors. This keeps ASP.NET, TUI, desktop shells, and
Svelte/React adapters aligned.

Fork groups are global session facts. Active path choices are selected-thread
facts. A selected path can pass through many message-boundary choices, and a
choice can be represented by the exact selected thread or by an ancestor member
whose descendant is selected.

```ts
nav.forkGroups; // all durable/global choice groups in the session
nav.activePathChoices; // only the choices reached by the selected path

nav.activePathChoices[0].selectedMember; // group member representing the path
nav.activePathChoices[0].selectedThreadId; // actual selected leaf thread
nav.activePathChoices[0].relationship; // 'exact-member' | 'descendant-of-member'
```

A group should render inline only if the selected thread path actually passes
through that choice point. If a user forked earlier and later reaches the same
numeric message position, that is not the same branch choice. The branch
navigator filters those unrelated groups out before the message-row selectors
place controls.

For timeline branch controls, derive render units from the active path plus the
rendered timeline. Key UI by `control.groupId`, never by message index. This
matters because timeline rows include work groups and runtime requests, so
transcript message indexes are not rendered row indexes:

```ts
import {
  getThreadBranchChoiceControlsForTimeline,
  getThreadBranchChoiceControlLabel,
} from '@hpd-research/hpd-agent-headless-ui';

const timelineControls = getThreadBranchChoiceControlsForTimeline(nav, timeline);

for (const control of timelineControls) {
  renderInlineForkPager({
    key: control.groupId,
    timelineItemId: control.renderTimelineItemId,
    timelineIndex: control.renderTimelineIndex,
    placement: control.renderPlacement,
    label: getThreadBranchChoiceControlLabel(control),
    previousThreadId: control.previous?.threadId,
    nextThreadId: control.next?.threadId,
  });
}
```

Controls keep both facts separate:

- `boundaryMessageId` is the durable lineage boundary, meaning the last copied
  shared message before divergence.
- `selectedMember.choiceMessageId` is the preferred message row anchor for the
  selected path.
- `selectedMember.choiceMessageIndex` is the member-local transcript index
  fallback when the selected path is a descendant and the ancestor member's
  exact message id is not present in the rendered timeline.
- `choiceMessageIndex` mirrors the selected member's visual index for
  display/debugging.
- `renderTimelineItemId` is the rendered timeline row that matches
  the selected member anchor.

This is intentional for edit/retry UX. Editing a later user prompt copies
history through the previous assistant answer, but the inline switcher belongs
beside the user prompt that changed.

The selector does not guess from boundary ids or from a global group row. It
first looks for `selectedMember.choiceMessageId`. If that exact row is not in a
descendant timeline, it can use `selectedMember.choiceMessageIndex`. If the
selected member has no anchor, no inline control is returned for that group.
Timeline placements are `'root'`, `'choice-message'`, or `'unplaced'`.

Inline branch switchers are timeline controls. Do not place branch switchers
from transcript indexes.

## Selectors

Common selectors:

```ts
getThreadTimeline(snapshot)
getThreadWorkGroups(snapshot)
getTranscriptMessages(snapshot)

getLatestMessage(snapshot)
getLastUserMessage(snapshot)
getLastAssistantMessage(snapshot)
getMessageById(snapshot, messageId)
getMessageStatus(message)

getActiveToolCalls(snapshot)
isToolCallActive(toolCall)
getToolCallDuration(toolCall)

getPendingRuntimeRequests(snapshot)
hasPendingRuntimeRequests(snapshot)
getBlockingRuntimeRequests(snapshot)

isThreadBusy(snapshot)
canSubmitText(snapshot)
getTextSubmissionState(snapshot)

hasForkGroups(nav)
hasActivePathChoices(nav)
getBranchChoicePosition(activeChoice)
getBranchChoiceLabel(activeChoice)
getThreadBranchChoiceControlsForTimeline(nav, timeline)
getThreadBranchChoiceControlLabel(control)

isSubAgentThread(thread)
isMainAgentThread(thread)
isHiddenThread(thread)
isVisibleThread(thread)
getSubAgentRuntimeChildren(nav)
getVisibleRuntimeChildren(nav)
getInspectableRuntimeChildren(nav)
getRuntimeChildGroups(nav)
```

Array-returning selectors return shallow copies or freshly filtered arrays so
framework adapters do not accidentally mutate snapshot internals.

## Interrupting Work

`interrupt()` expresses user intent to stop active work.

```ts
await thread.interrupt({ reason: 'User cancelled' });
```

The UI should wait for thread-run events or a later rehydration to confirm final
status. The interrupt call expresses intent; thread-run state reports the
durable outcome.

## Connection Ownership

By default, a controller assumes it owns a dedicated `AgentClient` connection.

```ts
await thread.disconnect();
```

If a caller owns a shared client, opt out of stopping the client:

```ts
const thread = createThreadController({
  client,
  agentId,
  sessionId,
  threadId,
  stopClientOnDisconnect: false,
});
```

Then `disconnect()` detaches this controller's listeners without stopping the
shared transport.

## Strict Scope Defaults

Live event projection is strict by default.

```ts
allowScopeLessEvents: false
```

Events with mismatched `agentId`, `sessionId`, or `threadId` are ignored.
Scope-less events are ignored unless explicitly enabled.

## Boundary

The headless core owns lifecycle projection and selectors. It does not own:

- framework components
- styling
- DOM measurement
- global active thread state
- workspace/session caches
- protocol event reconstruction
- app-specific grouping policy

Those decisions belong in adapters or applications. The core gives them enough
timeline structure to make those decisions without reverse-engineering raw
events.
