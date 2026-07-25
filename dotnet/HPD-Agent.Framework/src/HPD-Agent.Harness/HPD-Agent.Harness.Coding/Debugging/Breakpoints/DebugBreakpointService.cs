using System.Collections.Immutable;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugBreakpointSnapshot(
    DebugDesiredBreakpointSnapshot Desired,
    ImmutableArray<DebugBreakpointBindingState> AdapterStates,
    DebugBreakpointCounts Counts,
    string? DebugSessionId,
    bool DetailsRetained);

/// <summary>Committed semantic breakpoint selection and its adapter bindings.</summary>
internal sealed record DebugBreakpointMutationResult(
    DebugBreakpointKind Kind,
    DebugDesiredBreakpointSnapshot Before,
    DebugDesiredBreakpointSnapshot After,
    ImmutableArray<DebugBreakpointBindingState> Bindings,
    DebugBreakpointCounts Counts,
    string DebugSessionId);

internal sealed class DebugBreakpointService(DebugSessionManager sessions)
{
    private readonly DebugSessionManager _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));

    public DebugBreakpointSnapshot GetSnapshot(
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId = null)
    {
        if (_sessions.TryResolveTerminal(owner, treeId, out var terminal))
            return new DebugBreakpointSnapshot(
                new(),
                [],
                terminal.Breakpoints,
                null,
                false);
        var (tree, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.Inspect);
        var desired = tree.Breakpoints.Snapshot;
        var adapterStates = session.AdapterBreakpoints.Snapshot;
        var requested = desired.Source.Length + desired.Function.Length +
            desired.Exception.Length + desired.Instruction.Length + desired.Data.Length;
        var acknowledged = adapterStates.Count(item => item.Acknowledged);
        var verified = adapterStates.Count(item => item.Verified);
        var hits = session.AdapterBreakpoints.HitCounts;
        return new DebugBreakpointSnapshot(
            desired,
            adapterStates,
            new DebugBreakpointCounts(
                requested,
                acknowledged,
                verified,
                Math.Max(0, requested - verified),
                hits.Hit,
                hits.Unknown),
            session.SessionId,
            true);
    }

    public async ValueTask<DebugBreakpointMutationResult> SetSourceAsync(DebugTreeLookupScope owner, string treeId, string? sessionId,
        IReadOnlyList<DebugSourceBreakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        var (tree, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.SourceBreakpoints);
        ImmutableArray<DebugBreakpointBindingState> committedBindings = [];
        var mutation = await tree.Breakpoints.ReplaceSourceAsync(breakpoints, async (prior, replacement, ct) =>
        {
            var paths = prior.Select(x => x.Path).Concat(replacement.Select(x => x.Path)).Distinct(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                var requested = replacement.Where(x => string.Equals(x.Path, path, StringComparison.Ordinal)).ToArray();
                var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetBreakpointsRequest,
                    new SetBreakpointsArguments
                    {
                        Source = new Source { Path = path },
                        Breakpoints = requested.Select(x => new SourceBreakpoint
                        {
                            Line = x.Line, Column = x.Column, Condition = x.Condition,
                            HitCondition = x.HitCondition, LogMessage = x.LogMessage
                        }).ToList()
                    }, ct).ConfigureAwait(false);
                session.AdapterBreakpoints.ReplaceSource(path, requested, response.Breakpoints);
            }
            committedBindings = session.AdapterBreakpoints.Snapshot;
        }, cancellationToken).ConfigureAwait(false);
        return CreateMutationResult(DebugBreakpointKind.Source, mutation, session, committedBindings);
    }

    public async ValueTask<DebugBreakpointMutationResult> SetFunctionAsync(DebugTreeLookupScope owner, string treeId, string? sessionId,
        IReadOnlyList<DebugFunctionBreakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        var (tree, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.FunctionBreakpoints);
        RequireCapability(session, capability => capability.SupportsFunctionBreakpoints == true,
            "function breakpoints");
        ImmutableArray<DebugBreakpointBindingState> committedBindings = [];
        var mutation = await tree.Breakpoints.ReplaceFunctionAsync(breakpoints, async (_, replacement, ct) =>
        {
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetFunctionBreakpointsRequest,
                new SetFunctionBreakpointsArguments { Breakpoints = replacement.Select(x => new FunctionBreakpoint
                { Name = x.Name, Condition = x.Condition, HitCondition = x.HitCondition }).ToList() }, ct).ConfigureAwait(false);
            session.AdapterBreakpoints.ReplaceFunction(replacement, response.Breakpoints);
            committedBindings = session.AdapterBreakpoints.Snapshot;
        }, cancellationToken).ConfigureAwait(false);
        return CreateMutationResult(DebugBreakpointKind.Function, mutation, session, committedBindings);
    }

    public async ValueTask<DebugBreakpointMutationResult> SetExceptionAsync(DebugTreeLookupScope owner, string treeId, string? sessionId,
        IReadOnlyList<DebugExceptionFilter> filters, CancellationToken cancellationToken = default)
    {
        var (tree, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.ExceptionBreakpoints);
        ImmutableArray<DebugBreakpointBindingState> committedBindings = [];
        var mutation = await tree.Breakpoints.ReplaceExceptionAsync(filters, async (_, replacement, ct) =>
        {
            var breakpoints = await DebugExceptionBreakpointProtocol.ApplyAsync(
                session, replacement, ct).ConfigureAwait(false);
            session.AdapterBreakpoints.ReplaceException(replacement, breakpoints);
            committedBindings = session.AdapterBreakpoints.Snapshot;
        }, cancellationToken).ConfigureAwait(false);
        return CreateMutationResult(DebugBreakpointKind.Exception, mutation, session, committedBindings);
    }

    public async ValueTask<DebugBreakpointMutationResult> SetInstructionAsync(DebugTreeLookupScope owner, string treeId, string? sessionId,
        IReadOnlyList<DebugInstructionBreakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        var (tree, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.InstructionBreakpoints);
        RequireCapability(session, capability => capability.SupportsInstructionBreakpoints == true,
            "instruction breakpoints");
        ImmutableArray<DebugBreakpointBindingState> committedBindings = [];
        var mutation = await tree.Breakpoints.ReplaceInstructionAsync(breakpoints, async (_, replacement, ct) =>
        {
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetInstructionBreakpointsRequest,
                new SetInstructionBreakpointsArguments { Breakpoints = replacement.Select(x => new InstructionBreakpoint
                { InstructionReference = x.InstructionReference, Offset = x.Offset, Condition = x.Condition, HitCondition = x.HitCondition }).ToList() }, ct).ConfigureAwait(false);
            session.AdapterBreakpoints.ReplaceInstruction(replacement, response.Breakpoints);
            committedBindings = session.AdapterBreakpoints.Snapshot;
        }, cancellationToken).ConfigureAwait(false);
        return CreateMutationResult(DebugBreakpointKind.Instruction, mutation, session, committedBindings);
    }

    public ValueTask<DebugBreakpointMutationResult> SetInstructionTokensAsync(
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId,
        IReadOnlyList<(string Token, long? Offset, string? Condition, string? HitCondition)> breakpoints,
        CancellationToken cancellationToken = default)
    {
        var (_, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.InstructionBreakpoints);
        RequireCapability(session, capability => capability.SupportsInstructionBreakpoints == true,
            "instruction breakpoints");
        var resolved = breakpoints.Select(item => new DebugInstructionBreakpoint(
            session.Projections.ResolveTextToken(item.Token, "instruction", out _, out _),
            item.Offset,
            item.Condition,
            item.HitCondition,
            Portable: false)).ToArray();
        return SetInstructionAsync(owner, treeId, session.SessionId, resolved, cancellationToken);
    }

    public async ValueTask<DebugBreakpointMutationResult> SetDataAsync(DebugTreeLookupScope owner, string treeId, string? sessionId,
        IReadOnlyList<DebugDataBreakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        var (tree, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.DataBreakpoints);
        RequireCapability(session, capability => capability.SupportsDataBreakpoints == true,
            "data breakpoints");
        ImmutableArray<DebugBreakpointBindingState> committedBindings = [];
        var mutation = await tree.Breakpoints.ReplaceDataAsync(breakpoints, async (_, replacement, ct) =>
        {
            foreach (var item in replacement)
                if (item.OriginSessionId is { } origin && !string.Equals(origin, session.SessionId, StringComparison.Ordinal))
                    throw new InvalidOperationException("A session-bound data breakpoint identity cannot be used in another protocol session.");
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetDataBreakpointsRequest,
                new SetDataBreakpointsArguments { Breakpoints = replacement.Select(x => new DataBreakpoint
                { DataId = x.DataId, AccessType = x.AccessType is null ? null : new(x.AccessType), Condition = x.Condition, HitCondition = x.HitCondition }).ToList() }, ct).ConfigureAwait(false);
            session.AdapterBreakpoints.ReplaceData(replacement, response.Breakpoints);
            committedBindings = session.AdapterBreakpoints.Snapshot;
        }, cancellationToken).ConfigureAwait(false);
        return CreateMutationResult(DebugBreakpointKind.Data, mutation, session, committedBindings);
    }

    public ValueTask<DebugBreakpointMutationResult> SetDataTokensAsync(
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId,
        IReadOnlyList<(string Token, string? AccessType, string? Condition, string? HitCondition)> breakpoints,
        CancellationToken cancellationToken = default)
    {
        var (_, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.DataBreakpoints);
        RequireCapability(session, capability => capability.SupportsDataBreakpoints == true,
            "data breakpoints");
        var resolved = breakpoints.Select(item => new DebugDataBreakpoint(
            session.Projections.ResolveDataBreakpointToken(item.Token),
            item.AccessType,
            item.Condition,
            item.HitCondition,
            Portable: false,
            OriginSessionId: session.SessionId)).ToArray();
        return SetDataAsync(owner, treeId, session.SessionId, resolved, cancellationToken);
    }

    private static DebugBreakpointMutationResult CreateMutationResult(
        DebugBreakpointKind kind,
        DebugDesiredBreakpointMutation mutation,
        DebugSession session,
        ImmutableArray<DebugBreakpointBindingState> bindings)
    {
        var requested = mutation.After.Source.Length + mutation.After.Function.Length +
            mutation.After.Exception.Length + mutation.After.Instruction.Length + mutation.After.Data.Length;
        var acknowledged = bindings.Count(item => item.Acknowledged);
        var verified = bindings.Count(item => item.Verified);
        return new DebugBreakpointMutationResult(
            kind,
            mutation.Before,
            mutation.After,
            bindings,
            new DebugBreakpointCounts(
                requested,
                acknowledged,
                verified,
                Math.Max(0, requested - verified)),
            session.SessionId);
    }

    private static void RequireCapability(
        DebugSession session,
        Func<Capabilities, bool> predicate,
        string operation)
    {
        if (session.Capabilities is null || !predicate(session.Capabilities))
            throw new DebugSemanticException(
                DebugSemanticFailureReason.CapabilityUnavailable,
                $"The adapter does not support {operation}.");
    }

    private (DebugSessionTree Tree, DebugSession Session) Resolve(
        DebugTreeLookupScope owner, string treeId, string? sessionId, DebugTreeGrant grant)
    {
        var tree = _sessions.ResolveTree(owner, treeId);
        tree.RuntimeBinding.State.ThrowIfUnavailable();
        tree.Authorization.Demand(grant);
        return (tree, tree.SelectSession(sessionId));
    }
}
