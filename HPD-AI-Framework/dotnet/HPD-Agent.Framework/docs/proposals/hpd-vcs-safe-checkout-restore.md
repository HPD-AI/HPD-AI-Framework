# HPD-VCS Safe Checkout and Restore

## Summary

Harden `HPD-VCS` so it can safely serve as the snapshot and restore substrate for agent workspace branching, without losing user files or silently advancing state after partial restore failures.

This proposal is infrastructure-focused. It is not specific to the coding harness. The coding harness snapshot timeline should depend on these safer primitives instead of carrying restore safety logic in agent middleware.

## Motivation

The coding workspace snapshot timeline needs a provider that can:

- capture a workspace tree
- compare two captured trees
- restore a captured tree
- refuse unsafe restores
- preserve untracked user files
- report conflicts clearly

`HPD-VCS` already has most of the foundation: content-addressed objects, trees, snapshots, checkout, ignore rules, status, and diffs. During review, however, `ExplicitSnapshotWorkingCopy.CheckoutAsync` showed behavior that is unsafe for branch rewind.

A direct verification showed that checking out an older tree can recursively delete a tracked directory and remove an untracked file inside it while reporting success:

```text
stats removed=1 skipped=0
UNTRACKED_DELETED
```

That behavior is too risky for agent-driven workspace restore. A branch rewind must fail closed if it cannot prove the restore is safe.

## Current State

Relevant code:

- `WorkingCopy/CheckoutOperations.cs`
- `WorkingCopy/ExplicitSnapshotWorkingCopy.cs`
- `WorkingCopy/IWorkingCopy.cs`
- `Repository.cs`
- `Diffing/UnifiedDiffFormatter.cs`
- `WorkingCopy/WorkingCopyStateTests.cs`
- `WorkingCopy/CheckoutAdvancedScenariosTests.cs`
- `Module8Tests.cs`

Current strengths:

- `SnapshotAsync` handles added, modified, deleted, ignored, large, and non-ASCII files.
- `SnapshotOptions` supports max new file size, ignore behavior, dry-run mode, and custom new-file tracking.
- `CheckoutAsync` can materialize nested directories.
- Checkout has some conflict and partial-failure tests.
- Repository status and diff APIs cover several common workflows.

Current gaps:

- `dotnet test` built `HPD-VCS.Tests`, but reported no discoverable tests in this environment.
- `CheckoutAsync` updates tracked file states and `CurrentTreeId` even after skipped files.
- Directory deletion uses recursive delete and can remove untracked files inside a tracked directory.
- `GetWorkingCopyDiffAsync` creates a real snapshot with `DryRun = false`, so a diff preview can mutate working-copy snapshot state and write objects.
- There is no public non-mutating tree-to-tree diff API.
- There is no strict restore mode designed for branch rewind.

## Goals

- Make checkout safe by default for untracked user content.
- Add strict restore semantics suitable for agent workspace rewind.
- Add non-mutating tree diff APIs.
- Fix test discovery so `HPD-VCS.Tests` can be trusted.
- Keep agent-specific policy outside `HPD-VCS`.

## Non-Goals

- Do not implement the coding harness snapshot middleware here.
- Do not add branch/session semantics to `HPD-VCS`.
- Do not make `HPD-VCS` depend on `HPD-Agent`.
- Do not require the user workspace to be a git repository.
- Do not remove best-effort checkout behavior if existing callers need it.

## Design Principles

- Never silently delete user files.
- Prefer fail-closed restore behavior for automation.
- Keep generic VCS safety in `HPD-VCS`.
- Keep agent-specific decisions in `HpdVcsWorkspaceSnapshotService`.
- Separate preview operations from mutating operations.
- Make partial restore explicit and observable.

## Proposed API Changes

### CheckoutOptions

Extend `CheckoutOptions`:

```csharp
public record CheckoutOptions
{
    public bool ForceOverwriteUntracked { get; init; } = false;
    public bool PreserveUntrackedFiles { get; init; } = true;
    public bool FailOnSkippedFiles { get; init; } = false;
    public bool PreflightOnly { get; init; } = false;
}
```

Default behavior should preserve untracked files. Existing best-effort callers can continue using non-strict options. Agent branch restore should use:

```csharp
new CheckoutOptions
{
    PreserveUntrackedFiles = true,
    ForceOverwriteUntracked = false,
    FailOnSkippedFiles = true
}
```

### CheckoutResult

`CheckoutStats` is useful, but strict restore needs richer conflict reporting. Add a richer result type or extend the existing stats path:

```csharp
public sealed record CheckoutResult
{
    public CheckoutStats Stats { get; init; }
    public IReadOnlyList<CheckoutConflict> Conflicts { get; init; } = [];
    public bool Succeeded => Stats.FilesSkipped == 0 && Conflicts.Count == 0;
}

public sealed record CheckoutConflict
{
    public RepoPath Path { get; init; }
    public CheckoutConflictKind Kind { get; init; }
    public string Message { get; init; } = "";
}

public enum CheckoutConflictKind
{
    UntrackedFileWouldBeOverwritten,
    UntrackedDirectoryWouldBeOverwritten,
    UntrackedContentWouldBeDeleted,
    FileLocked,
    AccessDenied,
    TypeChangeBlocked
}
```

This can be introduced as `CheckoutSafeAsync` first to avoid breaking existing callers, then folded back into `CheckoutAsync` later.

### Tree Diff API

Expose a public non-mutating tree diff:

```csharp
public Task<Dictionary<RepoPath, string>> GetTreeDiffAsync(
    TreeId baseTreeId,
    TreeId targetTreeId);
```

This should reuse the existing private tree diff implementation without scanning the working copy, writing objects, or changing current working-copy state.

### Raw Tree Restore API

Expose a restore operation that accepts a raw tree:

```csharp
public Task<CheckoutResult> CheckoutTreeAsync(
    TreeId targetTreeId,
    CheckoutOptions options);
```

This API should not require creating commits or advancing user-facing branch metadata. It is the primitive the coding snapshot provider should use for restoring captured snapshots.

## Safe Checkout Behavior

Before writing, deleting, or replacing anything, checkout should preflight the affected paths.

Unsafe operations:

- deleting a directory that contains untracked files
- replacing an untracked file
- replacing an untracked directory
- changing a file to a directory when the existing path contains untracked content
- changing a directory to a file when the directory contains untracked content
- writing over a locked or inaccessible file

When unsafe and `ForceOverwriteUntracked` is false:

- do not mutate that path
- record a skipped file and a conflict
- if `FailOnSkippedFiles` is true, abort the checkout before mutating anything

When unsafe and `ForceOverwriteUntracked` is true:

- proceed, but report the overwritten/deleted paths in stats or conflicts for audit

## Directory Deletion

Replace recursive directory deletion with tracked-entry-aware deletion.

Current unsafe pattern:

```csharp
Directory.Delete(path, recursive: true);
```

Required behavior:

```text
for each tracked child in the old tree:
  delete or update only that tracked child

after tracked children are removed:
  remove directory only if it is empty

if untracked children remain:
  leave directory in place
  report UntrackedContentWouldBeDeleted
```

This preserves user-created files that happen to live under a directory HPD-VCS previously tracked.

## State Advancement

Strict restore must not advance state after partial failure.

Current behavior updates tracked state after checkout even when skipped files exist. For strict restore:

```text
preflight
if conflicts:
  return failed result without mutation

apply mutations
if any mutation fails:
  return failed result
  do not update CurrentTreeId
  do not replace tracked file states

if complete:
  replace tracked file states
  update CurrentTreeId
```

Best-effort checkout may keep partial-success semantics, but strict restore should not.

## Diff Preview

`Repository.GetWorkingCopyDiffAsync` currently creates a real snapshot with `DryRun = false`. That is not suitable for preview-only flows.

Required changes:

- add `GetTreeDiffAsync(TreeId, TreeId)`
- add `GetWorkingCopyDiffAsync` overload or implementation path that uses dry-run and does not advance state
- remove debug `Console.WriteLine` output from library diff methods

Agent UI should be able to preview changes without creating new snapshot objects or mutating working-copy state.

## Test Discovery

`HPD-VCS.Tests` should be made executable under normal repo commands.

Likely updates:

```xml
<PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
<Using Include="Xunit" />
```

Acceptance:

```bash
dotnet test dotnet/shared/test/HPD-VCS.Tests/HPD-VCS.Tests.csproj
```

must discover and run the test suite.

## Implementation Plan

### Phase 1: Test Harness Repair

- Update test project runner configuration.
- Run existing tests.
- Fix any compile/discovery issues.

Acceptance:

- `dotnet test` discovers tests.
- Existing failures are visible and actionable.

### Phase 2: Conflict Model and Options

- Extend `CheckoutOptions`.
- Add `CheckoutConflict` and `CheckoutResult`.
- Add tests for the new option defaults.

Acceptance:

- Strict and best-effort checkout modes are represented explicitly.

### Phase 3: Preflight

- Implement checkout preflight traversal.
- Detect untracked overwrite/delete/type-change conflicts.
- Add `PreflightOnly` behavior.

Acceptance:

- Preflight reports conflicts without changing disk state.

### Phase 4: Safe Directory Deletion

- Replace recursive directory delete with tracked-entry-aware delete.
- Preserve untracked files inside tracked directories.
- Report skipped/conflict state when untracked content blocks deletion.

Acceptance:

- Restoring to a tree without a directory does not delete untracked files inside that directory.

### Phase 5: Strict Restore State Semantics

- Prevent `CurrentTreeId` and file-state replacement after strict restore failure.
- Keep best-effort behavior available if required.

Acceptance:

- Strict restore failure leaves `CurrentTreeId` unchanged.
- Strict restore failure leaves tracked file state unchanged.

### Phase 6: Non-Mutating Diffs

- Expose `GetTreeDiffAsync`.
- Add non-mutating working-copy diff preview path.
- Remove debug console output from diff methods.

Acceptance:

- Diff preview does not write objects.
- Diff preview does not alter working-copy state.

## Test Plan

Add or repair tests for:

- test discovery under `dotnet test`
- added file snapshot
- modified file snapshot
- deleted file snapshot
- ignored file snapshot
- large new file skipped by size policy
- nested directory snapshot
- nested directory checkout
- safe checkout refuses to overwrite untracked file
- safe checkout refuses to overwrite untracked directory
- safe checkout refuses to delete untracked files inside a tracked directory
- strict checkout does not mutate disk when preflight conflicts exist
- strict checkout does not update `CurrentTreeId` after skipped files
- best-effort checkout behavior remains available
- tree-to-tree diff returns added, modified, and deleted paths
- tree-to-tree diff does not write objects
- working-copy diff preview does not advance snapshot state

## Relationship to Coding Workspace Snapshots

Once this work is complete, `HpdVcsWorkspaceSnapshotService` can be simple:

```text
TrackAsync -> SnapshotAsync
PatchAsync -> GetTreeDiffAsync + path summary
RestoreAsync -> CheckoutTreeAsync with strict options
RevertAsync -> targeted strict restore from source snapshots
DiffAsync -> GetTreeDiffAsync
```

The agent layer should not need to know how to protect untracked files. It should only decide when to snapshot, which tree to restore, and how to persist branch events.

## Open Questions

- Should strict checkout be a new method or an option on existing checkout?
- Should preflight return conflicts only, or also predicted stats?
- Should `CheckoutStats.FilesSkipped` count directories, files, or both?
- Should force-overwrite behavior record explicit audit entries?
- Should large tracked files be restored by default if they already exist in a tree?
- Should raw tree restore live on `Repository`, `IWorkingCopy`, or both?

## Recommended First Slice

Start with the safety bug:

```text
fix test discovery
add strict checkout option
add test for untracked file inside deleted tracked directory
replace recursive directory delete with safe tracked deletion
verify strict checkout does not advance state on conflict
```

This removes the main blocker to using `HPD-VCS` as the coding workspace restore engine.
