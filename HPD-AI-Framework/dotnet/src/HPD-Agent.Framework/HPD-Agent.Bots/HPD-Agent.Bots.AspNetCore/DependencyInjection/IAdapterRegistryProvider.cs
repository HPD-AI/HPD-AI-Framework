namespace HPD.Agent.Bots.AspNetCore;

/// <summary>
/// Provides the collection of registered adapter descriptors.
/// Implement this interface and register it in DI to make adapters discoverable
/// by <c>MapHPDBots()</c>.
/// </summary>
/// <remarks>
/// The source generator emits an implementation of this interface
/// (<c>GeneratedBotRegistryProvider</c>) and registers it automatically
/// when <c>AddXxxBot()</c> is called.
/// </remarks>
public interface IBotRegistryProvider
{
    /// <summary>Returns all registered adapter descriptors.</summary>
    IEnumerable<BotRegistration> GetAll();
}
