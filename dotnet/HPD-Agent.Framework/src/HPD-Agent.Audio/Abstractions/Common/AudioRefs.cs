using System.Text.Json;

namespace HPD.Agent.Audio;

public sealed record InputContentSourceRef(
    string SourceKind,
    string? Name,
    string? MediaType,
    long? SizeBytes,
    string? Sha256);

public sealed record InputContentStoreRef(
    string StoreKind,
    string? Scope,
    string ContentId,
    string? Version,
    Uri? ReadUri);

public sealed record AudioArtifactRef(
    string Store,
    string ArtifactId,
    string? MediaType,
    long? SizeBytes,
    string? Sha256);

public sealed record ThreadRef(string SessionId, string ThreadId);

public sealed record ThreadProjectedEventRef(string EventId, long SequenceNumber);

public sealed record ProviderStateRef(string ProviderKey, string StateKind, string? RefId);

public sealed record ProviderMediaRef(string ProviderKey, string MediaId, string? MediaType);

public sealed record ProviderItemRef(string ProviderKey, string ItemId, string? ResponseId);

public sealed record AudioSemanticEventRef(string EventType, string? EventId, JsonElement? Payload);

public sealed record ToolResultPayload(JsonElement? JsonValue, string? ContentType = null);
