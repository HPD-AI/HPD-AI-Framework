namespace HPD.Agent.Adapters.AspNetCore;

/// <summary>
/// Provides the collection of registered adapter descriptors.
/// Implement this interface and register it in DI to make adapters discoverable
/// by <c>MapHPDAdapters()</c>.
/// </summary>
/// <remarks>
/// The source generator emits an implementation of this interface
/// (<c>GeneratedAdapterRegistryProvider</c>) and registers it automatically
/// when <c>AddXxxAdapter()</c> is called.
/// </remarks>
public interface IAdapterRegistryProvider
{
    /// <summary>Returns all registered adapter descriptors.</summary>
    IEnumerable<AdapterRegistration> GetAll();
}
