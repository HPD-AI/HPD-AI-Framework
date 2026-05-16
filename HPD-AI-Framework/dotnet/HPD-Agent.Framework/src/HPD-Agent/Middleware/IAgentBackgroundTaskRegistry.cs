namespace HPD.Agent.Middleware;

/// <summary>
/// Runtime-owned registry for background work started by middleware or function bodies.
/// </summary>
public interface IAgentBackgroundTaskRegistry
{
    void RegisterBackgroundTask(Task task);

    void RegisterBackgroundTask(Func<CancellationToken, Task> taskFactory);

    void RegisterBackgroundTask(
        string name,
        FunctionInvocationSnapshot invocation,
        Func<FunctionBackgroundContext, CancellationToken, Task> taskFactory);
}
