namespace HPD.Events;

public static class EventCoordinatorExtensions
{
    public static Task RunAsync(this IEventCoordinator coordinator, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(coordinator);

        return Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }
}
