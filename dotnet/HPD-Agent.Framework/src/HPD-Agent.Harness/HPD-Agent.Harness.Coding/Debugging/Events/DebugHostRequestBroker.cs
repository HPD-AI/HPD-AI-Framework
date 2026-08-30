using System.Collections.Immutable;
using HPD.Agent;
using HPD.Events;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public sealed record DebugRunInTerminalRequestEvent : AgentEvent, IAgentRequestEvent<DebugRunInTerminalResponseEvent>
{
    public override EventKind Kind { get; init; } = EventKind.Control;
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public required string DebugRequestId { get; init; }
    public required string DebugTreeId { get; init; }
    public required string DebugSessionId { get; init; }
    public string? TerminalKind { get; init; }
    public string? Title { get; init; }
    public required string WorkingDirectory { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required IReadOnlyDictionary<string, string?> EnvironmentDelta { get; init; }
    public bool ArgsCanBeInterpretedByShell { get; init; }
    public string RequestId => DebugRequestId;
    public string SourceName => "HPD.Debugging";
}

public sealed record DebugRunInTerminalResponseEvent : AgentEvent, IAgentResponseEvent
{
    public override EventKind Kind { get; init; } = EventKind.Control;
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public required string DebugRequestId { get; init; }
    public int? ProcessId { get; init; }
    public int? ShellProcessId { get; init; }
    public string? ResponderId { get; init; }
    public string RequestId => DebugRequestId;
    public string SourceName => "HPD.Debugging.Host";
}

internal interface IDebugHostRequestBroker
{
    ValueTask<DebugRunInTerminalResponseEvent> RequestRunInTerminalAsync(
        DebugEventScope scope,
        string debugTreeId,
        string debugSessionId,
        string? terminalKind,
        string? title,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environmentDelta,
        bool argsCanBeInterpretedByShell,
        CancellationToken cancellationToken);

    ValueTask<RespondResult> RespondAsync(
        DebugRunInTerminalResponseEvent response,
        CancellationToken cancellationToken = default);
}

internal sealed class DebugHostRequestBroker : IDebugHostRequestBroker
{
    private readonly IEventCoordinator _events;
    private readonly IAgentEventPublisher? _threadEvents;
    private readonly TimeSpan _timeout;

    public DebugHostRequestBroker(
        IEventCoordinator events,
        IAgentEventPublisher? threadEvents,
        TimeSpan? timeout = null)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _threadEvents = threadEvents;
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async ValueTask<DebugRunInTerminalResponseEvent> RequestRunInTerminalAsync(
        DebugEventScope scope,
        string debugTreeId,
        string debugSessionId,
        string? terminalKind,
        string? title,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environmentDelta,
        bool argsCanBeInterpretedByShell,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugTreeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environmentDelta);
        if (arguments.Count == 0 || string.IsNullOrWhiteSpace(arguments[0]))
            throw new InvalidOperationException("runInTerminal requires args[0] to be an executable or command.");

        var request = new DebugRunInTerminalRequestEvent
        {
            DebugRequestId = Guid.NewGuid().ToString("N"),
            DebugTreeId = debugTreeId,
            DebugSessionId = debugSessionId,
            TerminalKind = terminalKind,
            Title = title,
            WorkingDirectory = workingDirectory,
            Arguments = arguments.ToImmutableArray(),
            EnvironmentDelta = environmentDelta.ToImmutableDictionary(StringComparer.Ordinal),
            ArgsCanBeInterpretedByShell = argsCanBeInterpretedByShell,
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            TraceId = scope.TraceId
        };
        var handle = _events.RegisterRequest<DebugRunInTerminalRequestEvent, DebugRunInTerminalResponseEvent>(
            request, new RequestOptions { Timeout = _timeout, CancellationToken = cancellationToken });
        try
        {
            if (_threadEvents is not null)
                await _threadEvents.CommitAndPublishAsync(new(scope.SessionId, scope.ThreadId), request, cancellationToken).ConfigureAwait(false);
            else
                await _events.EmitAsync(request, cancellationToken).ConfigureAwait(false);
            return (DebugRunInTerminalResponseEvent)await handle.Response.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            handle.Cancel("Debug host request publication or wait failed.");
            throw;
        }
    }

    public ValueTask<RespondResult> RespondAsync(
        DebugRunInTerminalResponseEvent response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        return _events.RespondAsync(response.DebugRequestId, response, async (accepted, _) =>
        {
            if (_threadEvents is null) return accepted;
            var scoped = (DebugRunInTerminalResponseEvent)accepted;
            return await _threadEvents.CommitAndPublishAsync(
                new(scoped.SessionId!, scoped.ThreadId!), scoped, CancellationToken.None).ConfigureAwait(false);
        }, cancellationToken);
    }
}
