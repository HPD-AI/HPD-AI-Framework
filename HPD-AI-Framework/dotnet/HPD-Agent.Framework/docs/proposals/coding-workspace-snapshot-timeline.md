# Coding Workspace Snapshot Timeline

## Summary

Add a workspace snapshot timeline for the coding harness so HPD can track, summarize, revert, unrevert, and branch file-system changes made during agent coding sessions.

The goal is parity with the opencode-style workflow where each assistant step has a start snapshot, finish snapshot, and durable patch record. When a user edits a previous message or forks a branch, HPD should be able to restore the workspace to the matching conversation point, not just fork the chat transcript.

This should be implemented as harness-scoped middleware plus a provider-neutral snapshot service. `EditFile` and `WriteFile` should continue to emit detailed mutation metadata, but they should not own branch rewind semantics.

Production revert, unrevert, and fork-from-message workspace restore depend on two infrastructure proposals being completed first:

- `hpd-vcs-safe-checkout-restore.md`
- `agent-branch-lifecycle-hooks.md`

## Grounding

This proposal is grounded in HPD infrastructure that already exists:

- `CodingHarness` is a collapsed harness with harness-scoped middleware.
- `EnvironmentContextMiddleware` already observes coding tool metadata and updates middleware state after `ReadFile`, `EditFile`, and `WriteFile`.
- `CodingLanguageServerMiddleware` already observes the same tool lifecycle and reacts to reads/mutations.
- `ApplyTextMutationAsync` already records before/after content, text edits, changed line ranges, diff stats, and mutation events.
- `FileMutationAppliedEvent` already persists to branch history.
- `AgentContext`, `HookContext`, and `AgentMiddlewarePipeline` already provide lifecycle hooks, typed contexts, state updates, and event emission.
- `MiddlewareState` already supports transient, branch-scoped persistent, and session-scoped persistent state.
- `Branch.MiddlewareState` is copied on fork and can diverge independently.
- `dotnet/shared/src/HPD-VCS` already provides a native content-addressed VCS library with object storage, tree snapshots, working-copy scan/status, checkout, ignore handling, and unified diff formatting.

Additional infrastructure required before production workspace restore:

- `HPD-VCS` must provide safe checkout/restore primitives that fail closed around untracked files and partial restore conflicts.
- the agent middleware pipeline must expose branch fork lifecycle hooks so middleware can know when a fork operation is happening, not merely that a branch already exists.
- coding-layer infrastructure must expose workspace restore orchestration and bidirectional restore permission events. Core `HPD.Agent` should not own workspace concepts.

Reference implementation behavior observed in opencode:

- A hidden snapshot service stores worktree state in a separate git directory.
- A snapshot is captured before an assistant step starts.
- A snapshot is captured after an assistant step finishes.
- A patch part records the changed files since the start snapshot.
- Revert walks conversation parts after the target point, collects patch records, and restores affected files from their original snapshot.
- Unrevert restores the worktree snapshot captured immediately before the revert.

The HPD implementation should borrow the shape, not the TypeScript implementation.

## Motivation

HPD can already fork and branch conversation state. For coding agents, conversation branching is incomplete unless the workspace can branch too.

Desired user behavior:

```text
User asks agent to edit files
Agent changes the workspace
User edits a previous message or forks from an earlier point
HPD restores the workspace to the matching point
New branch can diverge independently
User can unrevert if needed
UI can show the files changed by each assistant step/message
```

Without a workspace timeline, HPD can preserve the chat transcript but leave the actual files in the latest state. That creates confusing branches where the model sees old conversation context against a newer file system.

## Non-Goals

- Do not move edit safety rules into the snapshot layer.
- Do not remove existing `FileMutationAppliedEvent` records.
- Do not require a real user git repository.
- Do not make this feature specific to `EditFile` and `WriteFile`.
- Do not make command execution snapshots depend on shell output parsing.
- Do not persist large file contents directly in middleware state.
- Do not make snapshots part of ordinary LLM-visible tool output.

## Design Principles

- Track workspace state at conversation boundaries, not only at tool boundaries.
- Keep detailed mutation audit events separate from reversible workspace state.
- Prefer HPD's native VCS primitives before introducing an external hidden-git dependency.
- Keep provider details behind an interface so alternative snapshot stores can exist.
- Record enough branch events to replay and inspect the timeline without reading tool output.
- Make revert/unrevert explicit session operations, not model tools by default.

## Dependencies

This proposal has three layers:

```text
Layer 1: HPD-VCS Safe Checkout and Restore
  safe restore primitives, non-mutating diffs, strict checkout behavior

Layer 2: Agent Branch Lifecycle Hooks
  branch fork hooks and branch operation reason/context

Layer 3: Coding Workspace Snapshot Timeline
  coding middleware, snapshot timeline state, coding patch events, workspace restore hooks/events, UI summaries
```

Layer 3 can be prototyped with a fake snapshot provider, but it should not ship production revert, unrevert, or fork-from-message restore until Layers 1 and 2 are implemented.

## Architecture

```text
CodingHarness
  ReadFile / EditFile / WriteFile / ExecuteCommand / search tools
  emit tool results, metadata, and mutation events

CodingWorkspaceSnapshotMiddleware
  captures start/end snapshots around coding iterations or tool batches
  records patch events into branch history
  updates branch-scoped workspace timeline state

IWorkspaceSnapshotService
  tracks worktree snapshots
  computes patches and file diffs
  restores snapshots
  reverts patch groups

Agent branch lifecycle infrastructure
  exposes BeforeBranchForkAsync / AfterBranchForkAsync
  exposes branch fork reason and operation metadata

Session / Branch layer
  exposes revert, unrevert, and fork-from-message operations
  persists branch timeline events and current revert metadata

Coding workspace restore infrastructure
  exposes coding-owned workspace restore operations
  emits workspace restore started/completed/failed events
  supports bidirectional restore permission requests

UI / client
  displays changed files per step/message
  offers revert/unrevert/fork controls
```

## Snapshot Service

Introduce a runtime capability or injectable service:

```csharp
public interface IWorkspaceSnapshotService
{
    ValueTask<WorkspaceSnapshot?> TrackAsync(
        WorkspaceSnapshotRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkspacePatch> PatchAsync(
        WorkspacePatchRequest request,
        CancellationToken cancellationToken = default);

    ValueTask RestoreAsync(
        WorkspaceRestoreRequest request,
        CancellationToken cancellationToken = default);

    ValueTask RevertAsync(
        WorkspaceRevertRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<WorkspaceFileDiff>> DiffAsync(
        WorkspaceDiffRequest request,
        CancellationToken cancellationToken = default);
}
```

Provider:

```text
HpdVcsWorkspaceSnapshotService
```

This provider should adapt `HPD-VCS` instead of shelling out to `git`. `HPD-VCS` already has most of the storage and working-copy primitives required for opencode-style snapshots:

- `Repository` coordinates object storage, operations, views, commits, checkout, status, and diffs.
- `IWorkingCopy` exposes `SnapshotAsync`, `CheckoutAsync`, `CreateSnapshotAsync`, and `UpdateCurrentTreeIdAsync`.
- `ExplicitSnapshotWorkingCopy` performs on-demand recursive snapshots with ignore rules, size thresholds, file-state tracking, and dry-run support.
- `SnapshotStats` reports untracked, modified, deleted, ignored, and skipped files.
- `CheckoutStats` reports files updated, added, removed, skipped, and materialized conflicts.
- `GetCommitDiffAsync` and `GetWorkingCopyDiffAsync` already produce path-keyed unified diffs.

The coding harness should use `ExplicitSnapshotWorkingCopy` semantics for deterministic middleware boundaries. Avoid `LiveWorkingCopy` for the first version; a file watcher is useful for interactive VCS workflows, but the coding harness wants snapshots exactly at model/tool lifecycle boundaries.

Conceptual mapping:

```text
TrackAsync
  ensure an HPD-VCS repository exists for the workspace root or snapshot store
  run explicit SnapshotAsync with coding snapshot options
  store the resulting TreeId as the workspace snapshot id

PatchAsync(fromSnapshot, toSnapshot)
  compare the two TreeId values
  return changed paths, status categories, and optional unified diffs

RestoreAsync(snapshot)
  checkout the snapshot TreeId into the workspace
  return CheckoutStats

RevertAsync(patches)
  restore affected files from their source snapshots
  delete files absent from the source snapshot

DiffAsync(fromSnapshot, toSnapshot)
  generate path-keyed unified diffs using HPD-VCS diffing
```

The provider may need a small adapter layer because `Repository` currently centers operations around commits and the default workspace. For this feature, the durable identity can be either:

- a lightweight HPD-VCS commit per timeline boundary, or
- a raw `TreeId` snapshot persisted in branch events.

Raw `TreeId` snapshots are the better first fit because the conversation branch timeline already owns causality. Commits can be added later if we want VCS-level history browsing independent of branch events.

Snapshot provider requirements:

- Respect workspace roots.
- Respect ignore rules by default.
- Avoid tracking very large files.
- Avoid device paths, external paths, and workspace escapes.
- Lock per workspace root while snapshotting or restoring.
- Support multiple roots by tracking each root independently or by using a synthetic multi-root snapshot id.
- Work when the user repository is not a git repository.
- Do not require the `git` executable.

HPD-VCS integration requirements:

- Add a project/package reference from the coding snapshot implementation to `HPD-VCS`.
- Store snapshot data outside the user workspace when possible, or clearly isolate `.hpd` metadata if in-workspace storage is used.
- Ensure `.hpd`, `.git`, build output, dependency caches, and configured ignored paths are excluded by default.
- Verify nested directory snapshots and checkout behavior with coding-harness tests.
- Expose a direct tree-to-tree diff API if using `Repository.GetCommitDiffAsync` would force unnecessary commits.
- Add a tree checkout operation that can restore a raw `TreeId` without advancing user-facing VCS branch state.
- Harden checkout/restore so it never deletes or overwrites untracked user files unless explicitly forced.

## HPD-VCS Readiness Notes

`HPD-VCS` is the right substrate, but it is not ready to use as a branch-rewind restore engine without a hardening pass.

Confirmed strengths:

- `ExplicitSnapshotWorkingCopy` has tests for added, modified, deleted, ignored, large, and non-ASCII files.
- `SnapshotOptions` already supports `MaxNewFileSize`, ignore rules, dry-run, and a new-file tracking predicate.
- `CheckoutAsync` has tests for nested directory materialization, file/directory swaps, skipped write conflicts, and partial failure handling.
- Repository-level status and diff APIs already cover untracked, modified, deleted, mixed changes, binary files, and large-file diff summaries.

Gaps found during review:

- The existing `HPD-VCS.Tests` project built, but `dotnet test` reported no discoverable tests in this environment. Test discovery should be fixed before relying on the suite as a quality gate.
- `ExplicitSnapshotWorkingCopy.CheckoutAsync` updates tracked state and `CurrentTreeId` even when `FilesSkipped > 0`. That is acceptable for a best-effort checkout command, but branch restore should fail closed instead.
- Directory deletion currently uses recursive delete for tracked directories that disappear from the target tree. A direct verification showed this can delete untracked files inside that directory while reporting `FilesSkipped = 0`.
- `Repository.GetWorkingCopyDiffAsync` creates a real snapshot with `DryRun = false`, so using it for UI diff previews can mutate snapshot state and write objects.

Required hardening before provider integration:

- Add a safe checkout mode that preflights deletions and type changes.
- Refuse to remove a directory if it contains files not present in the current tracked tree.
- Refuse to overwrite an untracked path unless an explicit force option is provided.
- Treat any skipped file during restore as a failed restore and leave branch timeline state unchanged.
- Provide tree-to-tree diff helpers that do not mutate working-copy state.
- Add executable tests that cover untracked files inside deleted directories, partial restore failure, raw `TreeId` restore, and no-object-write diff previews.

## Coding Middleware

Add a third scoped middleware to the coding harness:

```csharp
[Collapse(
    "...",
    SystemPrompt = CodingHarnessPrompts.SystemPrompt,
    Middlewares =
    [
        typeof(EnvironmentContextMiddleware),
        typeof(CodingLanguageServerMiddleware),
        typeof(CodingWorkspaceSnapshotMiddleware)
    ])]
public partial class CodingHarness
{
}
```

The middleware should capture snapshots around coding work. The preferred first slice is iteration-level:

```text
BeforeIterationAsync
  if coding harness is active and workspace exists:
    capture start snapshot
    store pending step in middleware state

AfterIterationAsync
  if pending step exists:
    capture finish snapshot
    compute patch from start snapshot
    emit branch-persisted snapshot/patch events
    clear pending step
```

Later, it may support finer boundaries:

- before/after parallel tool batches
- before/after individual functions
- command-specific snapshots for long-running background commands

Iteration-level tracking is the best first milestone because it catches all file changes, including shell commands, formatters, package managers, generators, `EditFile`, and `WriteFile`.

## Middleware State

Use branch-scoped state for conversation-derived snapshot timeline state:

```csharp
[MiddlewareState(Persistent = true, Scope = StateScope.Branch)]
public sealed record CodingWorkspaceTimelineState
{
    public IReadOnlyDictionary<string, WorkspaceRootTimeline> Roots { get; init; } = ...;
    public WorkspaceSnapshotStep? PendingStep { get; init; }
    public WorkspaceRevertState? ActiveRevert { get; init; }
}
```

Do not store large diffs or file contents in middleware state. Store compact ids, snapshot ids, patch ids, and references to branch events or artifact paths.

Suggested state records:

```csharp
public sealed record WorkspaceSnapshotStep
{
    public required string StepId { get; init; }
    public required string SessionId { get; init; }
    public required string BranchId { get; init; }
    public required string? MessageTurnId { get; init; }
    public required int Iteration { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required IReadOnlyList<WorkspaceRootSnapshot> StartSnapshots { get; init; }
}

public sealed record WorkspaceRootSnapshot
{
    public required string RootId { get; init; }
    public required string RootPath { get; init; }
    public required string SnapshotId { get; init; }
}

public sealed record WorkspaceRevertState
{
    public required string TargetMessageId { get; init; }
    public string? TargetPartId { get; init; }
    public required IReadOnlyList<WorkspaceRootSnapshot> PreRevertSnapshots { get; init; }
    public required DateTimeOffset RevertedAt { get; init; }
}
```

## Branch Events

Add branch-persisted events that mirror opencode's step-start, step-finish, and patch parts in HPD terms.

Suggested events:

```csharp
public sealed record CodingWorkspaceSnapshotStartedEvent : AgentEvent
{
    public override bool ShouldPersistToBranch() => true;
    public required string StepId { get; init; }
    public required string BranchId { get; init; }
    public required string? MessageTurnId { get; init; }
    public required int Iteration { get; init; }
    public required IReadOnlyList<WorkspaceRootSnapshot> Snapshots { get; init; }
}

public sealed record CodingWorkspaceSnapshotFinishedEvent : AgentEvent
{
    public override bool ShouldPersistToBranch() => true;
    public required string StepId { get; init; }
    public required IReadOnlyList<WorkspaceRootSnapshot> Snapshots { get; init; }
}

public sealed record CodingWorkspacePatchRecordedEvent : AgentEvent
{
    public override bool ShouldPersistToBranch() => true;
    public required string StepId { get; init; }
    public required IReadOnlyList<WorkspaceRootPatch> Patches { get; init; }
}
```

Patch shape:

```csharp
public sealed record WorkspaceRootPatch
{
    public required string RootId { get; init; }
    public required string FromSnapshotId { get; init; }
    public required string ToSnapshotId { get; init; }
    public required IReadOnlyList<WorkspaceChangedFile> Files { get; init; }
}

public sealed record WorkspaceChangedFile
{
    public required string Path { get; init; }
    public required WorkspaceFileChangeKind Kind { get; init; }
    public int AddedLines { get; init; }
    public int RemovedLines { get; init; }
}
```

The existing `FileMutationAppliedEvent` remains the detailed per-tool audit event. The new patch event is the reversible branch timeline event.

## Coding Workspace Restore Infrastructure

Workspace restore is coding-layer infrastructure. Core `HPD.Agent` should expose branch fork lifecycle hooks, but it should not define workspace roots, file snapshots, checkout, restore, or file overwrite permission semantics.

Add coding-owned restore operation models and events near the coding snapshot service:

```csharp
public sealed record CodingWorkspaceRestoreOperation
{
    public required string OperationId { get; init; }
    public required string SessionId { get; init; }
    public required string SourceBranchId { get; init; }
    public string? TargetBranchId { get; init; }
    public int? FromMessageIndex { get; init; }
    public string? FromMessageId { get; init; }
    public required CodingWorkspaceRestoreReason Reason { get; init; }
    public required IReadOnlyList<WorkspaceRootRestorePlan> Roots { get; init; }
    public bool MayDeleteFiles { get; init; }
    public bool MayOverwriteFiles { get; init; }
    public bool RequiresPermission { get; init; }
}

public enum CodingWorkspaceRestoreReason
{
    ForkFromMessage,
    Revert,
    Unrevert,
    ManualRestore,
    ProviderDefined
}
```

Suggested live/control events:

```csharp
public sealed record CodingWorkspaceRestorePermissionRequestEvent(
    string RequestId,
    CodingWorkspaceRestoreOperation Operation,
    CodingWorkspaceRestorePermissionPolicy Policy
) : AgentEvent, IBidirectionalAgentEvent;

public sealed record CodingWorkspaceRestorePermissionResponseEvent(
    string RequestId,
    CodingWorkspaceRestorePermissionDecision Decision,
    string? Reason = null
) : AgentEvent, IBidirectionalAgentEvent;
```

Suggested durable events:

```csharp
public sealed record CodingWorkspaceRestoreStartedEvent(
    CodingWorkspaceRestoreOperation Operation
) : AgentEvent;

public sealed record CodingWorkspaceRestoreCompletedEvent(
    CodingWorkspaceRestoreOperation Operation,
    CodingWorkspaceRestoreResult Result
) : AgentEvent;

public sealed record CodingWorkspaceRestoreFailedEvent(
    CodingWorkspaceRestoreOperation Operation,
    string ErrorMessage
) : AgentEvent;
```

These events belong to the coding snapshot feature because they talk about workspace roots, snapshots, changed paths, deleted paths, and checkout results.

## Revert and Unrevert

Add session/branch operations:

```csharp
ValueTask<Branch> RevertWorkspaceToMessageAsync(
    string sessionId,
    string branchId,
    string messageId,
    string? partId = null,
    CancellationToken cancellationToken = default);

ValueTask<Branch> UnrevertWorkspaceAsync(
    string sessionId,
    string branchId,
    CancellationToken cancellationToken = default);
```

Revert algorithm:

```text
assert branch is not actively running
load branch timeline events
find target message/part boundary
collect patch events after target boundary
capture pre-revert snapshot for unrevert
restore any existing active revert base if needed
emit CodingWorkspaceRestoreStartedEvent
request CodingWorkspaceRestorePermissionRequestEvent if required
call snapshotService.RevertAsync(patches)
emit CodingWorkspaceRestoreCompletedEvent or CodingWorkspaceRestoreFailedEvent
record active revert state
compute summary diff for UI
emit branch/session events
```

Unrevert algorithm:

```text
assert branch is not actively running
load active revert state
restore pre-revert snapshots
clear active revert state
emit branch/session events
```

Fork-from-message algorithm:

```text
execute branch fork lifecycle start
create branch from source branch conversation point
copy branch-scoped middleware state up to the fork point
resolve target boundary snapshot from copied timeline state/events
emit CodingWorkspaceRestoreStartedEvent
request CodingWorkspaceRestorePermissionRequestEvent if required
restore workspace to target boundary snapshot
emit CodingWorkspaceRestoreCompletedEvent or CodingWorkspaceRestoreFailedEvent
record fork metadata linking source branch, message id, and workspace snapshots
execute branch fork lifecycle end
```

The fork operation should not use session-scoped middleware state for workspace timeline data. Workspace timeline state is conversation-path state and should be branch-scoped.

## Relationship to Existing File Mutation Events

Existing coding mutation events answer:

```text
Which tool changed this exact file?
What old/new text was involved?
What hunks and diff stats were generated?
Was the file created or changed?
```

The workspace snapshot timeline answers:

```text
What was the workspace state before this assistant step?
What was the workspace state after this assistant step?
Which files changed across the whole step?
Can we restore or revert the workspace to a conversation point?
```

Both are needed.

`FileMutationAppliedEvent` should remain detailed and tool-adjacent. `CodingWorkspacePatchRecordedEvent` should remain compact and timeline-adjacent.

## Handling Shell Commands

The snapshot middleware should track shell side effects without understanding shell commands.

Example:

```text
BeforeIteration: snapshot A
ExecuteCommand: dotnet format
ExecuteCommand: dotnet test
AfterIteration: snapshot B
Patch(A, B): files changed by formatter/generated files
```

This is the core reason the feature belongs in middleware, not in `EditFile`.

Long-running background commands need additional care. Initial behavior can be:

- foreground commands are captured by normal iteration snapshots
- background commands are not attributed until a later completed iteration observes changed files
- future milestone can capture background command start/stop snapshots using `ExecuteCommandProcessStartedEvent` and `ExecuteCommandProcessExitedEvent`

## Security and Safety

The snapshot service must not widen file access beyond `AgentWorkspace`.

Rules:

- Resolve all snapshot roots from `AgentWorkspace`.
- Reject paths outside configured roots.
- Do not snapshot blocked device/system paths.
- Do not mutate notebook files through text edit assumptions.
- Honor ignore rules by default.
- Do not persist secrets or file contents in branch events unless explicitly configured.
- Use file paths relative to workspace root in persisted patch records.
- Treat restore/revert as write operations requiring the same permission model as file mutation.

## Configuration

Suggested options:

```csharp
public sealed record CodingWorkspaceSnapshotOptions
{
    public bool Enabled { get; init; } = true;
    public bool RespectIgnoreFiles { get; init; } = true;
    public long MaxTrackedFileBytes { get; init; } = 2 * 1024 * 1024;
    public TimeSpan SnapshotTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan RestoreTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
```

Builder integration:

```csharp
builder.WithHarness<CodingHarness>(options =>
{
    options.ConfigureScopedMiddleware<CodingWorkspaceSnapshotMiddleware>(
        new CodingWorkspaceSnapshotOptions
        {
            Enabled = true
        });
});
```

Default behavior should be enabled only when a workspace is selected and snapshot capability is available. If unavailable, the middleware should emit a diagnostic event and continue without failing coding tools.

## Implementation Plan

### Phase 0: Required Infrastructure

- Implement `HPD-VCS Safe Checkout and Restore`.
- Implement `Agent Branch Lifecycle Hooks`.
- Verify branch fork lifecycle hooks expose source branch, new branch id, message index, and reason.

Acceptance:

- HPD-VCS strict restore refuses unsafe deletes/overwrites and does not advance state after partial failure.
- Middleware can observe branch fork operations before and after the fork.
- Core `HPD.Agent` does not need to know about workspace roots, snapshots, or restore.

### Phase 1: Contract and In-Memory Fake

- Add snapshot contracts and simple models.
- Add fake in-memory snapshot provider for tests.
- Add branch events for snapshot start, finish, and patch recorded.
- Add coding-owned workspace restore started/completed/failed events.
- Add coding-owned workspace restore permission request/response events.
- Add serializer coverage for new events.
- Add `HPD-VCS` as the preferred concrete-provider dependency for later phases.

Acceptance:

- Tests can capture fake snapshots and emit patch events.
- Client code can approve or reject a destructive coding workspace restore.
- Events persist to branch history.
- No coding tool changes are required.

### Phase 2: Coding Middleware

- Implement `CodingWorkspaceSnapshotMiddleware`.
- Register it on `CodingHarness`.
- Capture iteration start/end snapshots.
- Compute patch after iteration.
- Update branch-scoped timeline state.

Acceptance:

- A coding run that edits a file produces start, finish, and patch events.
- A shell command that changes a file is captured even when no `EditFile` tool ran.
- No snapshots are created when no workspace exists.

### Phase 3: HPD-VCS Provider

- Fix HPD-VCS test discovery so the existing test suite actually runs under `dotnet test`.
- Add safe checkout/restore options to HPD-VCS.
- Add HPD-VCS tests for untracked files inside deleted tracked directories.
- Add HPD-VCS tests proving partial restore failure does not advance restore state.
- Implement `HpdVcsWorkspaceSnapshotService`.
- Use `ExplicitSnapshotWorkingCopy` style boundaries rather than live file watching.
- Store snapshot objects in an HPD-controlled VCS store.
- Track allowed files with ignore and size limits.
- Add direct tree-to-tree patch/diff helpers if needed.
- Implement `TrackAsync`, `PatchAsync`, `RestoreAsync`, `RevertAsync`, and `DiffAsync`.

Acceptance:

- Provider works in non-git workspaces.
- Provider does not require the `git` executable.
- Provider does not touch the user's `.git`.
- Restore refuses to delete or overwrite untracked files by default.
- Restore reports skipped/conflicting files as a failed branch restore.
- Added, modified, deleted, and moved files are represented accurately enough for UI and revert.
- Nested directory changes round-trip through snapshot, diff, and restore.

### Phase 4: Revert and Unrevert

- Add session/branch revert APIs.
- Collect patch events after target message/part.
- Capture pre-revert snapshots.
- Restore/revert workspace files.
- Use coding-owned workspace restore events and restore permission events.
- Store active revert state.
- Add unrevert API.

Acceptance:

- Reverting a message restores changed files.
- Unreverting restores the pre-revert workspace.
- Revert is rejected while branch execution is active.

### Phase 5: Fork From Message With Workspace Restore

- Extend branch fork/edit-previous-message flow to include workspace restore.
- Use branch fork lifecycle hooks to preserve fork operation context.
- Resolve target workspace snapshots from the copied branch-scoped timeline.
- Use coding-owned workspace restore events and restore permission events.
- Restore to target message boundary before continuing the new branch.
- Preserve session-scoped state and copy relevant branch-scoped state.

Acceptance:

- A branch forked from an earlier message sees matching file contents.
- New branch changes diverge without corrupting the source branch timeline.

### Phase 6: UI and Summary Support

- Expose changed files per assistant step/message.
- Expose session-level diff summaries.
- Add revert/unrevert controls in UI.
- Show snapshot unavailable diagnostics when disabled or unsupported.

Acceptance:

- UI can display changed files and diff stats per step/message.
- UI can revert/unrevert without model involvement.

## Test Plan

Unit tests:

- Middleware captures start and finish snapshots.
- Middleware skips capture without workspace.
- Middleware emits no patch when no files changed.
- Middleware emits patch when `EditFile` changes a file.
- Middleware emits patch when `ExecuteCommand` changes a file.
- State updates are branch-scoped and persistent only where expected.
- Event serialization round-trips.

Provider tests:

- HPD-VCS provider initializes isolated metadata.
- Track clean workspace.
- Track added file.
- Track modified file.
- Track deleted file.
- Track nested directory changes.
- Restore snapshot.
- Revert patch list.
- Ignore ignored files.
- Skip files above max size.
- Reject outside-workspace paths.
- Restore from raw snapshot tree without mutating user git state.
- Restore does not recursively delete untracked files inside a tracked directory.
- Restore does not advance branch restore state when checkout reports skipped files.
- Diff preview does not write new snapshot objects.

Session tests:

- Revert to earlier message restores files.
- Unrevert restores pre-revert state.
- Fork from previous message restores matching workspace snapshot.
- Revert is blocked while branch is busy.
- Branch copy preserves source branch timeline without sharing mutable state.

Integration tests:

- Run coding harness with `EditFile`, then revert.
- Run coding harness with `ExecuteCommand` creating a file, then revert.
- Run branch/edit-previous-message flow and verify file contents match the selected message point.

## Open Questions

- Should the first timeline boundary be iteration-level or message-turn-level?
- Should snapshot events be agent events only, branch events only, or both?
- Where should HPD-VCS snapshot object data live for hosted/server deployments?
- Should users be able to disable snapshots per workspace?
- Should restore/revert require explicit permission even if original edits were allowed?
- How should background command mutations be attributed?
- How should binary files be summarized?

## Recommended First Vertical Slice

Build the smallest useful path:

```text
Fake snapshot service
CodingWorkspaceSnapshotMiddleware
Iteration start/end events
Patch event with changed file list
Tests proving EditFile and ExecuteCommand changes are captured
```

After that, implement the HPD-VCS provider and only then wire revert/unrevert.

This gives HPD full parity with the opencode model while staying aligned with HPD's middleware architecture: tools report what they did, middleware records when it happened, and branch/session services decide how to rewind or fork the workspace.

Production fork-from-message restore should only be considered complete after both prerequisite infrastructure proposals are implemented:

- `hpd-vcs-safe-checkout-restore.md`
- `agent-branch-lifecycle-hooks.md`
