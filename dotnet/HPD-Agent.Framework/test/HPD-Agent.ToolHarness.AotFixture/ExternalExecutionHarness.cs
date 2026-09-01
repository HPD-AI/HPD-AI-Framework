using HPD.Agent;
using HPD.Agent.Middleware;

namespace HPD.Agent.ToolHarness.AotFixture;

[Collapse(
    "Cross-assembly Native AOT harness",
    FunctionResult = "expanded",
    Middlewares = [typeof(ExternalExecutionMiddleware)])]
public sealed partial class ExternalExecutionHarness
{
    [AIFunction]
    public string Ping() => "pong";
}

public sealed class ExternalExecutionMiddleware
    : IToolHarnessMiddleware, IToolHarnessMiddlewareLifecycle, IAsyncDisposable
{
    private static int _created;
    private static int _activated;
    private static int _disposed;

    public ExternalExecutionMiddleware() => Interlocked.Increment(ref _created);

    public static int CreatedCount => Volatile.Read(ref _created);
    public static int ActivatedCount => Volatile.Read(ref _activated);
    public static int DisposedCount => Volatile.Read(ref _disposed);

    public ValueTask OnHarnessActivatedAsync(
        ToolHarnessActivationContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activated);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnHarnessDeactivatingAsync(
        ToolHarnessDeactivationContext context,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposed);
        return ValueTask.CompletedTask;
    }
}
