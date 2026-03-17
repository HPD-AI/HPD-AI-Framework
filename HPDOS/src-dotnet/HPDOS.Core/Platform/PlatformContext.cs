using HPDOS.Core.Platform.Resources;

namespace HPDOS.Core.Platform;

/// <summary>
/// Context provided to applications during initialization.
/// </summary>
public sealed class PlatformContext
{
    public ResourceManager Resources { get; }
    public IEventEmitter? Emitter { get; }

    public PlatformContext(ResourceManager resources, IEventEmitter? emitter = null)
    {
        Resources = resources;
        Emitter = emitter;
    }

    public void Emit<T>(string eventName, T payload) where T : class
    {
        Emitter?.Emit(eventName, payload);
    }
}
