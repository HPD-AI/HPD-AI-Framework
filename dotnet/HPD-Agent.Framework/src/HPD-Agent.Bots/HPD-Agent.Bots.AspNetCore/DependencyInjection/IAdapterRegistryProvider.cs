namespace HPD.Agent.Bots.AspNetCore;

/// <summary>
/// Provides the collection of registered adapter descriptors.
/// Implement this interface and register it in DI to make adapters discoverable
/// by <c>MapHPDBots()</c>.
/// </summary>
/// <remarks>
/// Generated adapters register a generated implementation of this interface
/// when <c>AddXxxBot()</c> is called. Hand-written platform adapters can
/// implement and register their own provider.
/// </remarks>
public interface IBotRegistryProvider
{
    /// <summary>Returns all registered adapter descriptors.</summary>
    IEnumerable<BotRegistration> GetAll();
}
