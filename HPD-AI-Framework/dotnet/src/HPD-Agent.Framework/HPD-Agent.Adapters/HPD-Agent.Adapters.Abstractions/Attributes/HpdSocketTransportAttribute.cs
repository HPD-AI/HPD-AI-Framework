namespace HPD.Agent.Adapters;

/// <summary>
/// Declares that this adapter supports a persistent outbound WebSocket transport.
/// The source generator reads this and adds conditional BackgroundService registration
/// to the generated AddXxxAdapter() extension method.
/// </summary>
/// <param name="serviceType">
/// The BackgroundService type to register. Must extend <see cref="AdapterWebSocketService"/>.
/// Validated at compile time via diagnostic HPDA008.
/// </param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class HpdSocketTransportAttribute(Type serviceType) : Attribute
{
    /// <summary>The BackgroundService type to register (must extend AdapterWebSocketService).</summary>
    public Type ServiceType { get; } = serviceType;

    /// <summary>
    /// Name of the config property whose non-null value activates socket mode.
    /// Generated registration: if (cfg.{ConfigProperty} is not null) AddHostedService&lt;ServiceType&gt;()
    /// </summary>
    public string ConfigProperty { get; init; } = "";
}
