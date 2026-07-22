using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal static class DebugBreakpointProtocolApplier
{
    public static async Task ApplyAllAsync(
        DebugSession session,
        DebugDesiredBreakpointSnapshot desired,
        CancellationToken cancellationToken)
    {
        foreach (var group in desired.Source.GroupBy(x => x.Path, StringComparer.Ordinal))
        {
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetBreakpointsRequest,
                new SetBreakpointsArguments
                {
                    Source = new Source { Path = group.Key },
                    Breakpoints = group.Select(x => new SourceBreakpoint
                    {
                        Line = x.Line,
                        Column = x.Column,
                        Condition = x.Condition,
                        HitCondition = x.HitCondition,
                        LogMessage = x.LogMessage
                    }).ToList()
                }, cancellationToken).ConfigureAwait(false);
            session.ConfirmedBreakpoints.ReplaceSource(group.Key, response.Breakpoints);
        }

        if (!desired.Function.IsEmpty)
        {
            Require(session.Capabilities?.SupportsFunctionBreakpoints == true, "function");
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetFunctionBreakpointsRequest,
                new SetFunctionBreakpointsArguments
                {
                    Breakpoints = desired.Function.Select(x => new FunctionBreakpoint
                    {
                        Name = x.Name,
                        Condition = x.Condition,
                        HitCondition = x.HitCondition
                    }).ToList()
                }, cancellationToken).ConfigureAwait(false);
            session.ConfirmedBreakpoints.Replace(DebugBreakpointKind.Function, response.Breakpoints);
        }

        if (!desired.Exception.IsEmpty)
        {
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetExceptionBreakpointsRequest,
                new SetExceptionBreakpointsArguments
                {
                    Filters = desired.Exception.Select(x => x.FilterId).ToList(),
                    FilterOptions = desired.Exception.Where(x => x.Condition is not null).Select(x =>
                        new ExceptionFilterOptions { FilterId = x.FilterId, Condition = x.Condition }).ToList()
                }, cancellationToken).ConfigureAwait(false);
            session.ConfirmedBreakpoints.Replace(DebugBreakpointKind.Exception, response?.Breakpoints ?? []);
        }

        if (!desired.Instruction.IsEmpty)
        {
            Require(session.Capabilities?.SupportsInstructionBreakpoints == true, "instruction");
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetInstructionBreakpointsRequest,
                new SetInstructionBreakpointsArguments
                {
                    Breakpoints = desired.Instruction.Select(x => new InstructionBreakpoint
                    {
                        InstructionReference = x.InstructionReference,
                        Offset = x.Offset,
                        Condition = x.Condition,
                        HitCondition = x.HitCondition
                    }).ToList()
                }, cancellationToken).ConfigureAwait(false);
            session.ConfirmedBreakpoints.Replace(DebugBreakpointKind.Instruction, response.Breakpoints);
        }

        if (!desired.Data.IsEmpty)
        {
            Require(session.Capabilities?.SupportsDataBreakpoints == true, "data");
            foreach (var breakpoint in desired.Data)
            {
                if (breakpoint.OriginSessionId is { } owner &&
                    !string.Equals(owner, session.SessionId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Session-bound data breakpoint IDs must be rediscovered for a child session.");
            }
            var response = await session.Protocol.SendAsync(DebugProtocolDescriptors.SetDataBreakpointsRequest,
                new SetDataBreakpointsArguments
                {
                    Breakpoints = desired.Data.Select(x => new DataBreakpoint
                    {
                        DataId = x.DataId,
                        AccessType = x.AccessType is null ? null : new(x.AccessType),
                        Condition = x.Condition,
                        HitCondition = x.HitCondition
                    }).ToList()
                }, cancellationToken).ConfigureAwait(false);
            session.ConfirmedBreakpoints.Replace(DebugBreakpointKind.Data, response.Breakpoints);
        }
    }

    private static void Require(bool supported, string family)
    {
        if (!supported)
            throw new InvalidOperationException($"The adapter does not support requested {family} breakpoints.");
    }
}
