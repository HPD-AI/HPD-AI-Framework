namespace HPD.Agent.Middleware;

/// <summary>
/// Runtime-owned registry for background work started by middleware or function bodies.
/// </summary>
public interface IAgentBackgroundTaskRegistry
{
    BackgroundTaskRegistration RegisterBackgroundTask(
        BackgroundTaskDescriptor descriptor,
        Func<BackgroundTaskContext, CancellationToken, Task> taskFactory);
}

/// <summary>
/// Describes background work accepted by the runtime.
/// </summary>
/// <param name="TaskId">The runtime-generated background task id.</param>
/// <param name="Name">The background task name.</param>
/// <param name="SourceKind">The source category for the background task.</param>
public sealed record BackgroundTaskRegistration(
    string TaskId,
    string Name,
    BackgroundTaskSourceKind SourceKind);
