using System.Collections.Immutable;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugBreakpointSnapshot(
    DebugDesiredBreakpointSnapshot Desired,
    ImmutableArray<DebugAdapterBreakpointState> AdapterStates,
    DebugBreakpointCounts Counts,
    string? DebugSessionId,
    bool DetailsRetained);

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
        var verified = adapterStates.Count(item => item.Verified);
        return new DebugBreakpointSnapshot(
            desired,
            adapterStates,
            new DebugBreakpointCounts(
                requested,
                adapterStates.Length,
                verified,
                Math.Max(0, requested - verified)),
            session.SessionId,
            true);
    }

    public ValueTask SetSourceAsync(DebugTreeLookupScope owner, string treeId, string? sessionId,
        IReadOnlyList<DebugSourceBreakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        var (tree, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.SourceBreakpoints);
        return tree.Breakpoints.ReplaceSourceAsync(breakpoints, async (prior, replacement, ct) =>
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
                session.AdapterBreakpoints.ReplaceSource(path, response.Breakpoints);
            }
        }, cancellationToken);
    }

    public ValueTask SetFunctionAsync(DebugTreeLookupScope owner, string treeId, string? sessionId,
        IReadOnlyList<DebugFunctionBreakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        var (tree, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.FunctionBreakpoints);
        RequireCapability(session, capability => capability.SupportsFunctionBreakpoints == true,
            "function breakpoints");
        return tree.Breakpoints.ReplaceFunctionAsync(breakpoints, async (_, replacement, ct) =>
        {
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetFunctionBreakpointsRequest,
                new SetFunctionBreakpointsArguments { Breakpoints = replacement.Select(x => new FunctionBreakpoint
                { Name = x.Name, Condition = x.Condition, HitCondition = x.HitCondition }).ToList() }, ct).ConfigureAwait(false);
            session.AdapterBreakpoints.Replace(DebugBreakpointKind.Function, response.Breakpoints);
        }, cancellationToken);
    }

    public ValueTask SetExceptionAsync(DebugTreeLookupScope owner, string treeId, string? sessionId,
        IReadOnlyList<DebugExceptionFilter> filters, CancellationToken cancellationToken = default)
    {
        var (tree, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.ExceptionBreakpoints);
        return tree.Breakpoints.ReplaceExceptionAsync(filters, async (_, replacement, ct) =>
        {
            var breakpoints = await DebugExceptionBreakpointProtocol.ApplyAsync(
                session, replacement, ct).ConfigureAwait(false);
            session.AdapterBreakpoints.Replace(DebugBreakpointKind.Exception, breakpoints);
        }, cancellationToken);
    }

    public ValueTask SetInstructionAsync(DebugTreeLookupScope owner, string treeId, string? sessionId,
        IReadOnlyList<DebugInstructionBreakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        var (tree, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.InstructionBreakpoints);
        RequireCapability(session, capability => capability.SupportsInstructionBreakpoints == true,
            "instruction breakpoints");
        return tree.Breakpoints.ReplaceInstructionAsync(breakpoints, async (_, replacement, ct) =>
        {
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetInstructionBreakpointsRequest,
                new SetInstructionBreakpointsArguments { Breakpoints = replacement.Select(x => new InstructionBreakpoint
                { InstructionReference = x.InstructionReference, Offset = x.Offset, Condition = x.Condition, HitCondition = x.HitCondition }).ToList() }, ct).ConfigureAwait(false);
            session.AdapterBreakpoints.Replace(DebugBreakpointKind.Instruction, response.Breakpoints);
        }, cancellationToken);
    }

    public ValueTask SetInstructionTokensAsync(
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

    public ValueTask SetDataAsync(DebugTreeLookupScope owner, string treeId, string? sessionId,
        IReadOnlyList<DebugDataBreakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        var (tree, session) = Resolve(owner, treeId, sessionId, DebugTreeGrant.DataBreakpoints);
        RequireCapability(session, capability => capability.SupportsDataBreakpoints == true,
            "data breakpoints");
        return tree.Breakpoints.ReplaceDataAsync(breakpoints, async (_, replacement, ct) =>
        {
            foreach (var item in replacement)
                if (item.OriginSessionId is { } origin && !string.Equals(origin, session.SessionId, StringComparison.Ordinal))
                    throw new InvalidOperationException("A session-bound data breakpoint identity cannot be used in another protocol session.");
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetDataBreakpointsRequest,
                new SetDataBreakpointsArguments { Breakpoints = replacement.Select(x => new DataBreakpoint
                { DataId = x.DataId, AccessType = x.AccessType is null ? null : new(x.AccessType), Condition = x.Condition, HitCondition = x.HitCondition }).ToList() }, ct).ConfigureAwait(false);
            session.AdapterBreakpoints.Replace(DebugBreakpointKind.Data, response.Breakpoints);
        }, cancellationToken);
    }

    public ValueTask SetDataTokensAsync(
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
