# Agent Branch Lifecycle Hooks

## Summary

Add first-class middleware lifecycle hooks for branch operations.

Today, agent middleware can observe runtime, turn, iteration, and function execution. It can also read the current `Session` and `Branch`. That is enough to know that a branch exists, but not enough to know that a branch is currently being forked, why it is being forked, or which message boundary is being used.

This proposal keeps core `HPD.Agent` focused on agent/session/branch concepts. It does not add workspace, file-system restore, or coding-specific semantics to the core middleware lifecycle.

## Motivation

Conversation branching is an agent framework concept. Middleware may need to participate when the framework forks a branch for reasons such as:

- manual branch creation
- editing a previous message
- retrying from an earlier message
- restoring a conversation point
- provider-defined branch operations

Without branch lifecycle hooks, middleware can only infer branch ancestry after the branch already exists. That loses the operation context that existed at the fork boundary.

## Current State

Current middleware hooks cover:

- runtime start/stop
- message turn start/end
- model iteration start/end
- tool/function execution
- error handling

Current branch infrastructure covers:

- `ForkBranchAsync` creates a new branch from a source branch and message index.
- branch-scoped middleware state is copied from the source branch.
- session-scoped middleware state is shared.
- `BranchForkedEvent` records source branch id and message index.
- `BranchTreeUpdatedEvent` records sibling/tree navigation metadata.

Current gaps:

- no middleware hook runs around branch fork
- no context object describes the branch operation
- middleware cannot distinguish manual fork from edit-previous-message fork
- middleware cannot attach branch-operation metadata before the fork is persisted

## Goals

- Let middleware observe branch fork operations before and after they happen.
- Keep branch lifecycle operations outside model-visible tool execution.
- Preserve the current ordinary turn/iteration/function middleware model.
- Expose source branch, new branch id, fork message index, and reason.
- Keep branch-scoped state as conversation-path state.
- Keep session-scoped state as user/environment policy state.

## Non-Goals

- Do not add workspace concepts to core `HPD.Agent`.
- Do not add file-system restore concepts to core `HPD.Agent`.
- Do not implement the coding workspace snapshot timeline here.
- Do not implement HPD-VCS safe restore here.
- Do not make branch fork operations into model tools.
- Do not require every middleware to implement branch hooks.

## Design Principles

- Branch lifecycle is framework infrastructure, not a tool call.
- Core branch hooks should describe branch operations only.
- Domain-specific systems can subscribe to branch hooks and run their own follow-up work.
- Branch operation metadata should be explicit and typed.
- Middleware hooks should remain optional default interface methods.

## Proposed Middleware Hooks

Extend `IAgentMiddleware` with branch lifecycle hooks:

```csharp
Task BeforeBranchForkAsync(
    BeforeBranchForkContext context,
    CancellationToken cancellationToken)
    => Task.CompletedTask;

Task AfterBranchForkAsync(
    AfterBranchForkContext context,
    CancellationToken cancellationToken)
    => Task.CompletedTask;
```

These hooks should be optional default interface methods, matching the current middleware style.

## Branch Fork Contexts

Suggested context shape:

```csharp
public sealed class BeforeBranchForkContext : HookContext
{
    public Branch SourceBranch { get; }
    public string NewBranchId { get; }
    public int FromMessageIndex { get; }
    public string? FromMessageId { get; }
    public BranchForkReason Reason { get; }
    public IDictionary<string, object?> Metadata { get; } = new Dictionary<string, object?>();

    internal BeforeBranchForkContext(
        AgentContext baseContext,
        Branch sourceBranch,
        string newBranchId,
        int fromMessageIndex,
        string? fromMessageId,
        BranchForkReason reason)
        : base(baseContext)
    {
        SourceBranch = sourceBranch;
        NewBranchId = newBranchId;
        FromMessageIndex = fromMessageIndex;
        FromMessageId = fromMessageId;
        Reason = reason;
    }
}

public sealed class AfterBranchForkContext : HookContext
{
    public Branch SourceBranch { get; }
    public Branch NewBranch { get; }
    public int FromMessageIndex { get; }
    public string? FromMessageId { get; }
    public BranchForkReason Reason { get; }
    public IReadOnlyDictionary<string, object?> Metadata { get; }

    internal AfterBranchForkContext(
        AgentContext baseContext,
        Branch sourceBranch,
        Branch newBranch,
        int fromMessageIndex,
        string? fromMessageId,
        BranchForkReason reason,
        IReadOnlyDictionary<string, object?> metadata)
        : base(baseContext)
    {
        SourceBranch = sourceBranch;
        NewBranch = newBranch;
        FromMessageIndex = fromMessageIndex;
        FromMessageId = fromMessageId;
        Reason = reason;
        Metadata = metadata;
    }
}

public enum BranchForkReason
{
    ManualFork,
    EditPreviousMessage,
    RetryFromMessage,
    RestoreConversationPoint,
    ProviderDefined
}
```

The contexts should expose the same base capabilities as ordinary hook contexts: session id, branch id, services, runtime capabilities, safe state reads/updates, and event emission.

`BeforeBranchForkContext` should use the source branch as the active branch. `AfterBranchForkContext` should expose both source and new branch identity.

## Operation Metadata

The `Metadata` dictionary is intentionally generic. Core `HPD.Agent` should not define workspace-specific fields.

Expected uses:

- attach provider-specific correlation ids
- attach UI operation ids
- attach caller-provided labels
- allow domain middleware to pass compact operation state from before to after

Rules:

- metadata values should be small and serializable when possible
- metadata should not contain file contents, diffs, binary payloads, or large objects
- framework code should not require domain-specific metadata keys

## Pipeline Execution

The public fork flow should be decomposed into an orchestration path:

```text
resolve source session/branch
build BeforeBranchForkContext
execute BeforeBranchForkAsync
create/copy branch transcript and branch-scoped middleware state
emit branch fork/tree events
build AfterBranchForkContext
execute AfterBranchForkAsync
return new branch
```

Implementation detail: the internal `ForkBranchAsync(Branch sourceBranch, string newBranchId, int fromMessageIndex, ...)` can either become the orchestrator or be split into:

```csharp
ForkBranchAsync(...)          // orchestrates lifecycle
ForkBranchCoreAsync(...)      // performs current copy/save behavior
```

Splitting keeps the current branch copy logic easier to test.

## State Model

Branch lifecycle hooks should use the same `MiddlewareState` container as ordinary middleware hooks.

Expected behavior:

- `BeforeBranchForkAsync` reads source branch state.
- branch copy creates the new branch with copied branch-scoped state.
- `AfterBranchForkAsync` can see both source and new branch identity.
- branch-scoped state remains copied-on-fork and then diverges.
- session-scoped state remains shared across branches.

Domain-specific middleware can use branch hooks to prepare or record later work, but core branch lifecycle should not assume any specific domain.

## Error Handling

Branch lifecycle hooks should follow existing middleware error behavior where possible.

Suggested rules:

- failure in `BeforeBranchForkAsync` aborts the fork
- failure during branch copy aborts the fork
- failure in `AfterBranchForkAsync` should not corrupt an already persisted branch
- errors should be observable through the existing middleware error path

Open implementation choice: whether `AfterBranchForkAsync` failures should be fatal to the caller or recorded as non-fatal diagnostics. The safer first implementation is to surface the error while preserving the already-created branch.

## Serialization

Add serialization support for:

- `BranchForkReason`
- any new branch lifecycle events if implementation chooses to emit diagnostic events

The existing `BranchForkedEvent` and `BranchTreeUpdatedEvent` should remain the durable branch history events.

## Test Plan

Unit tests:

- `BeforeBranchForkAsync` receives source branch, new branch id, message index, and reason.
- failure in `BeforeBranchForkAsync` prevents branch creation.
- branch-scoped middleware state is copied after the before hook.
- `AfterBranchForkAsync` receives source and new branch.
- metadata from the before hook is visible to the after hook.
- existing branch fork events still persist.
- existing branch tests remain compatible.

Integration tests:

- manual fork invokes branch lifecycle hooks in order.
- edit-previous-message fork passes `BranchForkReason.EditPreviousMessage`.
- retry-from-message fork passes `BranchForkReason.RetryFromMessage`.
- middleware can update branch-scoped state during fork lifecycle.

## Implementation Plan

### Phase 1: Context and Hook Contracts

- Add branch fork context classes.
- Add `BranchForkReason`.
- Extend `IAgentMiddleware` with optional default branch hooks.
- Extend `AgentMiddlewarePipeline` with dispatch methods.

Acceptance:

- Middleware can compile against the new hooks.
- Existing middleware remains source-compatible.

### Phase 2: Branch Fork Orchestration

- Split current fork logic into orchestration and core copy behavior.
- Call `BeforeBranchForkAsync` before creating the branch.
- Call `AfterBranchForkAsync` after branch creation.
- Add tests for hook order and failure behavior.

Acceptance:

- Existing branch fork tests still pass.
- New tests prove middleware receives fork metadata.

### Phase 3: Caller Reason Plumbing

- Update fork/edit/retry call sites to pass a `BranchForkReason`.
- Default unspecified calls to `ManualFork`.
- Preserve compatibility for existing public overloads.

Acceptance:

- Edit-previous-message flows are distinguishable from manual branch forks.
- Existing callers do not need to change.

## Dependency Relationship

This proposal should be implemented before the coding workspace snapshot timeline's production fork-from-message restore work.

The intended dependency order is:

```text
1. HPD-VCS Safe Checkout and Restore
2. Agent Branch Lifecycle Hooks
3. Coding Workspace Snapshot Timeline
```

The coding snapshot middleware can be prototyped with a fake snapshot service earlier, but production fork-from-message restore should wait until branch lifecycle hooks exist. Workspace/file restore details belong in the coding snapshot proposal, not in core `HPD.Agent`.

## Open Questions

- Should branch lifecycle hooks also cover branch deletion?
- Should branch lifecycle hooks also cover branch metadata updates?
- Should `AfterBranchForkAsync` errors fail the caller or become diagnostics?
- Should `BranchForkReason` be extensible via string instead of enum?
