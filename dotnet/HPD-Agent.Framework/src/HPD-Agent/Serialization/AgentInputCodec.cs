using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.Providers;

namespace HPD.Agent.Serialization;

/// <summary>Explicit provider-aware serialization authority for semantic agent inputs.</summary>
public sealed class AgentInputCodec
{
    private readonly IReadOnlyDictionary<Type, (string Discriminator, JsonTypeInfo TypeInfo)> _byType;
    private readonly IReadOnlyDictionary<string, (Type Type, JsonTypeInfo TypeInfo)> _byDiscriminator;

    /// <summary>Creates an input codec bound to the provider composition used by the target agent.</summary>
    public AgentInputCodec(ProviderComposition providerComposition)
    {
        ProviderComposition = providerComposition ?? throw new ArgumentNullException(nameof(providerComposition));
        var entries = new (Type Type, string Discriminator)[]
        {
            (typeof(UserMessagesInputEvent), EventTypes.Input.USER_MESSAGES_INPUT),
            (typeof(AudioSessionInputEvent), EventTypes.Input.AUDIO_SESSION_INPUT),
            (typeof(CompactThreadInputEvent), EventTypes.Input.COMPACT_THREAD_INPUT),
            (typeof(AgentOperationNotificationInputEvent), EventTypes.Input.AGENT_OPERATION_NOTIFICATION_INPUT),
            (typeof(ClientTools.ClientToolOperationOutcomeEvent), EventTypes.ClientTool.CLIENT_TOOL_BACKGROUND_OPERATION_OUTCOME)
        };
        _byType = entries.ToDictionary(
            static entry => entry.Type,
            static entry => (entry.Discriminator, RequireTypeInfo(entry.Type)));
        _byDiscriminator = entries.ToDictionary(
            static entry => entry.Discriminator,
            static entry => (entry.Type, RequireTypeInfo(entry.Type)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets the immutable provider composition used for run-config encoding.</summary>
    public ProviderComposition ProviderComposition { get; }

    /// <summary>Serializes one semantic input and its typed provider run configuration.</summary>
    public string Serialize(AgentInputEvent input, string version = "1.0")
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (!_byType.TryGetValue(input.GetType(), out var entry))
            throw new JsonException($"Agent input type '{input.GetType().FullName}' is not supported by this input codec.");
        var payload = JsonSerializer.Serialize(input, entry.TypeInfo);
        var prefix = $"\"version\":{JsonSerializer.Serialize(version, AgentEventJsonContext.Default.String)}," +
            $"\"type\":{JsonSerializer.Serialize(entry.Discriminator, AgentEventJsonContext.Default.String)}";
        var envelope = payload == "{}" ? $"{{{prefix}}}" : payload.Insert(1, prefix + ",");
        if (input.RunConfig is null)
            return envelope;
        var root = JsonNode.Parse(envelope) as JsonObject
            ?? throw new JsonException("Agent input did not serialize to an object.");
        root["runConfig"] = JsonNode.Parse(HpdAgentConfigSerializer.Serialize(input.RunConfig, ProviderComposition));
        return root.ToJsonString(AgentEventJsonContext.Default.Options);
    }

    /// <summary>Hydrates one semantic input and its typed provider run configuration.</summary>
    public AgentInputEvent Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("type", out var typeProperty) || typeProperty.ValueKind != JsonValueKind.String)
            throw new JsonException("Agent input envelope requires a string 'type' property.");
        var discriminator = typeProperty.GetString()!;
        if (!_byDiscriminator.TryGetValue(discriminator, out var entry))
            throw new JsonException($"Unknown agent input discriminator '{discriminator}'.");
        using var payload = StripEnvelope(document.RootElement, entry.TypeInfo);
        var input = payload.RootElement.Deserialize(entry.TypeInfo) as AgentInputEvent
            ?? throw new JsonException($"Agent input '{discriminator}' hydrated to an invalid value.");
        if (!document.RootElement.TryGetProperty("runConfig", out var runConfig) ||
            runConfig.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return input;
        return input with
        {
            RunConfig = HpdAgentConfigSerializer.DeserializeRunConfig(runConfig.GetRawText(), ProviderComposition)
        };
    }

    private static JsonTypeInfo RequireTypeInfo(Type type) =>
        AgentEventJsonContext.Default.GetTypeInfo(type)
        ?? throw new InvalidOperationException($"Agent input '{type.FullName}' has no source-generated JSON metadata.");

    private static JsonDocument StripEnvelope(JsonElement root, JsonTypeInfo typeInfo)
    {
        var known = typeInfo.Properties.Select(static property => property.Name).ToHashSet(StringComparer.Ordinal);
        var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.NameEquals("version") || property.NameEquals("type") || property.NameEquals("runConfig"))
                    continue;
                if (known.Contains(property.Name))
                    property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return JsonDocument.Parse(stream.ToArray());
    }
}
