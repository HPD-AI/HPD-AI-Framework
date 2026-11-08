using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Serialization;

namespace HPD.Agent;

/// <summary>Declares how a runtime extension schema may be used.</summary>
[Flags]
public enum RuntimeAgentExtensionUsage
{
    LiveOutput = 1,
    DurableOutput = 2,
    Input = 4
}

/// <summary>A bounded, self-contained payload schema registered at runtime.</summary>
public sealed record RuntimeAgentExtensionSchema
{
    /// <summary>Gets the tenant/application-owned namespace.</summary>
    public required string Namespace { get; init; }

    /// <summary>Gets the schema name within the namespace.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the immutable schema version.</summary>
    public required int Version { get; init; }

    /// <summary>Gets the allowed runtime uses.</summary>
    public required RuntimeAgentExtensionUsage Usage { get; init; }

    /// <summary>Gets a self-contained JSON Schema for the payload.</summary>
    public required JsonElement PayloadSchema { get; init; }

    /// <summary>Gets an optional human-readable description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the stable schema key.</summary>
    [JsonIgnore]
    public string Key => $"{Namespace}:{Name}@{Version}";
}

/// <summary>Server-side descriptor for one registered runtime schema.</summary>
public sealed record RuntimeAgentExtensionDescriptor(
    string Key,
    string Namespace,
    string Name,
    int Version,
    RuntimeAgentExtensionUsage Usage,
    string PayloadSchemaDigest,
    JsonElement PayloadSchema,
    string? Description);

/// <summary>Deterministic runtime extension catalog.</summary>
public sealed record RuntimeAgentExtensionCatalog(
    IReadOnlyList<RuntimeAgentExtensionDescriptor> Schemas,
    string Digest);

/// <summary>A validated live-only runtime extension event.</summary>
[EventType("RUNTIME_AGENT_LIVE_EVENT")]
public sealed record RuntimeAgentLiveEvent : AgentEvent
{
    public required string ExtensionNamespace { get; init; }
    public required string SchemaName { get; init; }
    public required int SchemaVersion { get; init; }
    public required string SchemaDigest { get; init; }
    public required JsonElement Payload { get; init; }

    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Content;
    public override HPD.Events.EventChannel Channel { get; init; } = HPD.Events.EventChannel.Synchronous;
}

/// <summary>A validated runtime extension event admitted to thread history.</summary>
[DurableEvent]
[EventType("RUNTIME_AGENT_DURABLE_EVENT")]
public sealed record RuntimeAgentDurableEvent : AgentEvent
{
    public required string ExtensionNamespace { get; init; }
    public required string SchemaName { get; init; }
    public required int SchemaVersion { get; init; }
    public required string SchemaDigest { get; init; }
    public required JsonElement Payload { get; init; }

    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Content;
    public override HPD.Events.EventChannel Channel { get; init; } = HPD.Events.EventChannel.Synchronous;
}

/// <summary>A validated runtime extension input dispatched to a trusted handler.</summary>
[AgentInput]
[EventType("RUNTIME_AGENT_INPUT")]
public sealed record RuntimeAgentInputEvent : AgentInputEvent
{
    public required string ExtensionNamespace { get; init; }
    public required string SchemaName { get; init; }
    public required int SchemaVersion { get; init; }
    public required string SchemaDigest { get; init; }
    public required JsonElement Payload { get; init; }
}

/// <summary>Trusted application handler for one registered runtime input schema.</summary>
public interface IRuntimeAgentInputHandler
{
    ValueTask<RuntimeAgentInputHandlingResult> HandleAsync(
        RuntimeAgentInputEvent input,
        RuntimeAgentInputContext context,
        CancellationToken cancellationToken);
}

/// <summary>Runtime-safe execution information supplied to an extension input handler.</summary>
public sealed record RuntimeAgentInputContext(
    string AgentName,
    string? SessionId,
    string? ThreadId,
    AgentRunConfig? RunConfig,
    IServiceProvider? Services);

/// <summary>Result returned by a trusted runtime input handler.</summary>
public sealed record RuntimeAgentInputHandlingResult(
    bool Accepted,
    string? ErrorCode = null)
{
    public static RuntimeAgentInputHandlingResult Success { get; } = new(true);
}
