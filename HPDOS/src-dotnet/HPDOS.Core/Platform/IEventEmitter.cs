namespace HPDOS.Core.Platform;

/// <summary>
/// Interface for emitting events to the frontend.
/// Implemented by MAUI HybridWebView bridge.
/// </summary>
public interface IEventEmitter
{
    void Emit<T>(string eventName, T payload) where T : class;
}
