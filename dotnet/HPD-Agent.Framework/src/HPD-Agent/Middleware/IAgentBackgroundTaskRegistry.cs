namespace HPD.Agent.Middleware;

/// <summary>
/// Runtime-owned registry for background work started by middleware or function bodies.
/// </summary>
public interface IAgentBackgroundTaskRegistry
{
    void RegisterBackgroundTask(
        BackgroundTaskDescriptor descriptor,
        Func<BackgroundTaskContext, CancellationToken, Task> taskFactory);
}
