namespace HPD.Agent.ToolHarness.Coding.Debugging;

public sealed record DebugSourceBreakpoint(string Path, long Line, long? Column = null, string? Condition = null, string? HitCondition = null, string? LogMessage = null);
public sealed record DebugFunctionBreakpoint(string Name, string? Condition = null, string? HitCondition = null);
public sealed record DebugExceptionFilter(string FilterId, string? Condition = null);
public sealed record DebugDataBreakpoint(
    string DataId,
    string? AccessType = null,
    string? Condition = null,
    string? HitCondition = null,
    bool Portable = false,
    bool CanPersist = false,
    DebugDataBreakpointRecipe? Recipe = null,
    string? OriginSessionId = null,
    long? SuspensionEpoch = null);
public sealed record DebugInstructionBreakpoint(string InstructionReference, long? Offset = null, string? Condition = null, string? HitCondition = null, bool Portable = false);

public sealed record DebugInitialConfiguration
{
    public IReadOnlyList<DebugSourceBreakpoint> SourceBreakpoints { get; init; } = [];
    public IReadOnlyList<DebugFunctionBreakpoint> FunctionBreakpoints { get; init; } = [];
    public IReadOnlyList<DebugExceptionFilter> ExceptionFilters { get; init; } = [];
    public IReadOnlyList<DebugDataBreakpoint> DataBreakpoints { get; init; } = [];
    public IReadOnlyList<DebugInstructionBreakpoint> InstructionBreakpoints { get; init; } = [];
    public bool StopOnEntry { get; init; }
}

/// <summary>
/// Coordinates launch/attach and initialized without assuming their relative ordering. One caller
/// owns configuration; duplicate initialized notifications observe the same completion task.
/// </summary>
internal sealed class DebugConfigurationCoordinator
{
    private readonly Func<CancellationToken, Task> _configure;
    private readonly CancellationToken _lifetime;
    private readonly TaskCompletionSource _configuration = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _launch = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _configurationStarted;

    public DebugConfigurationCoordinator(Func<CancellationToken, Task> configure, CancellationToken lifetime)
    {
        _configure = configure ?? throw new ArgumentNullException(nameof(configure));
        _lifetime = lifetime;
    }

    public Task ConfigurationCompletion => _configuration.Task;
    public Task LaunchCompletion => _launch.Task;

    public void ObserveInitialized()
    {
        if (Interlocked.CompareExchange(ref _configurationStarted, 1, 0) != 0) return;
        _ = ConfigureAsync();
    }

    public async Task RunLaunchAsync(Func<CancellationToken, Task> launch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);
        try
        {
            await launch(cancellationToken).ConfigureAwait(false);
            _launch.TrySetResult();
        }
        catch (Exception exception)
        {
            _launch.TrySetException(exception);
            _configuration.TrySetException(exception);
            throw;
        }
    }

    public async Task AwaitStartBoundaryAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(_launch.Task, _configuration.Task).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void ObserveTerminal(string reasonCode)
    {
        var exception = new DebugStartTerminatedException(reasonCode);
        _launch.TrySetException(exception);
        _configuration.TrySetException(exception);
    }

    private async Task ConfigureAsync()
    {
        try
        {
            await _configure(_lifetime).ConfigureAwait(false);
            _configuration.TrySetResult();
        }
        catch (Exception exception)
        {
            _configuration.TrySetException(exception);
        }
    }
}

public sealed class DebugStartTerminatedException(string reasonCode)
    : InvalidOperationException($"The adapter terminated before debug start completed ({reasonCode}).")
{
    public string ReasonCode { get; } = reasonCode;
}
