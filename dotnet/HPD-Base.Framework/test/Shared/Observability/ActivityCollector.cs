using System.Collections.Concurrent;
using System.Diagnostics;

namespace HPD.Base.Tests.Observability;

internal sealed class ActivityCollector : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _stopped = new();

    public ActivityCollector(string sourceName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => _stopped.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public Activity[] Stopped => _stopped.ToArray();

    public string[] Names => Stopped.Select(activity => activity.OperationName).ToArray();

    public void Dispose() => _listener.Dispose();
}
