using Rhodium.Events;
namespace Rhodium.Platform;

public readonly ref struct LifecycleContext
{
    internal LifecycleContext(LifecycleEvent evt)
    {
        Event = evt;
    }

    public LifecycleEvent Event { get; }
}
