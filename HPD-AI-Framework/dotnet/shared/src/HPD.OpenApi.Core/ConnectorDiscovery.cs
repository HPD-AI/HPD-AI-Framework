using System.Runtime.CompilerServices;

namespace HPD.OpenApi.Core;

/// <summary>
/// Global discovery registry for connector packages.
/// ModuleInitializers in connector packages register here on assembly load.
/// Mirrors ProviderDiscovery — same thread-safe static registry pattern.
///
/// Enables:
/// - Config-driven instantiation: create a connector from JSON without knowing the concrete type
/// - Marketplace listing: enumerate all installed connectors with display names and config schemas
/// - AOT-safe serialization: all config round-trips go through source-generated JsonSerializerContext
/// </summary>
public static class ConnectorDiscovery
{
    private static readonly Dictionary<string, ConnectorRegistration> s_connectors
        = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object s_lock = new();

    /// <summary>
    /// Called by connector package ModuleInitializers to register a connector.
    /// Thread-safe.
    /// </summary>
    public static void RegisterConnector(string connectorKey, ConnectorRegistration registration)
    {
        lock (s_lock)
        {
            s_connectors[connectorKey] = registration;
        }
    }

    /// <summary>
    /// Returns the registration for a connector key, or null if not registered.
    /// </summary>
    public static ConnectorRegistration? GetConnector(string connectorKey)
    {
        lock (s_lock)
        {
            return s_connectors.TryGetValue(connectorKey, out var reg) ? reg : null;
        }
    }

    /// <summary>
    /// Returns all registered connectors. Used for UI discovery and marketplace listing.
    /// </summary>
    public static IReadOnlyDictionary<string, ConnectorRegistration> GetAll()
    {
        lock (s_lock)
        {
            return new Dictionary<string, ConnectorRegistration>(s_connectors);
        }
    }

    /// <summary>
    /// Explicitly loads a connector assembly to trigger its ModuleInitializer.
    /// Required for NativeAOT or PublishSingleFile scenarios where assembly scanning
    /// does not fire automatically.
    /// </summary>
    public static void LoadConnector<TConnectorModule>() where TConnectorModule : class
    {
        RuntimeHelpers.RunModuleConstructor(typeof(TConnectorModule).Module.ModuleHandle);
    }
}

/// <summary>
/// Registration record for a connector package.
///
/// All delegates are AOT-safe — they use source-generated JsonSerializerContext instances
/// rather than reflection. Connector packages emit these via [ModuleInitializer] using
/// their per-connector [JsonSerializable] context.
/// </summary>
public sealed class ConnectorRegistration
{
    /// <summary>Display name shown in UIs (e.g., "Stripe").</summary>
    public required string DisplayName { get; init; }

    /// <summary>CLR type of the connector config (e.g., typeof(StripeConnectorConfig)).</summary>
    public required Type ConfigType { get; init; }

    /// <summary>
    /// Creates an <see cref="OpenApiCoreConfig"/> from a JSON config string and a secret resolver.
    /// The secret resolver is passed as <see cref="object"/> so that HPD.OpenApi.Core has zero
    /// dependency on HPD-Agent. Cast to <c>ISecretResolver</c> inside the lambda.
    ///
    /// AOT-safe: the lambda uses a source-generated JsonSerializerContext, not reflection.
    /// Enables FFI/UI layers to instantiate connectors from persisted JSON config without
    /// a compile-time reference to the concrete config type.
    /// </summary>
    public required Func<string, object, OpenApiCoreConfig> CreateCoreConfigFromJson { get; init; }

    /// <summary>AOT-safe deserializer for the connector config type.</summary>
    public required Func<string, object?> DeserializeConfig { get; init; }

    /// <summary>AOT-safe serializer for the connector config type.</summary>
    public required Func<object, string> SerializeConfig { get; init; }
}
