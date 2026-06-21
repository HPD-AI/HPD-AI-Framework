using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace HPD.Agent.Serialization;

/// <summary>
/// Provides Native AOT compatible JSON serialization for selected agent struct events.
/// </summary>
/// <remarks>
/// Struct event serialization is an explicit export surface. It does not make
/// <see cref="AgentStructEvent"/> values part of the hosted <see cref="AgentEvent"/> stream.
/// </remarks>
public static partial class AgentStructEventSerializer
{
    private static readonly Dictionary<Type, string> TypeNames = new();
    private static readonly Dictionary<string, Type> DiscriminatorToType =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Type, JsonTypeInfo> TypeInfos = new();

    /// <summary>
    /// Serializes an agent struct event to JSON with version and type fields.
    /// </summary>
    public static string ToJson<TEvent>(TEvent evt)
        where TEvent : struct, AgentStructEvent
    {
        return ToJson(evt, "1.0");
    }

    /// <summary>
    /// Serializes an agent struct event to JSON with a specified version.
    /// </summary>
    public static string ToJson<TEvent>(TEvent evt, string version)
        where TEvent : struct, AgentStructEvent
    {
        ArgumentNullException.ThrowIfNull(version);
        return ToJsonEnvelope(evt, typeof(TEvent), version);
    }

    /// <summary>
    /// Gets the type discriminator for a registered or convention-named struct event type.
    /// </summary>
    public static string GetEventTypeName(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return TypeNames.TryGetValue(eventType, out var typeName)
            ? typeName
            : ToScreamingSnakeCase(eventType.Name);
    }

    /// <summary>
    /// Gets the type discriminator for a struct event instance.
    /// </summary>
    public static string GetEventTypeName<TEvent>(TEvent evt)
        where TEvent : struct, AgentStructEvent
    {
        return GetEventTypeName(typeof(TEvent));
    }

    /// <summary>
    /// Registers an agent struct event type with a discriminator and optional JSON metadata.
    /// </summary>
    public static void RegisterEventType(Type eventType, string discriminator, JsonTypeInfo? typeInfo = null)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(discriminator);

        if (!eventType.IsValueType || !typeof(AgentStructEvent).IsAssignableFrom(eventType))
            throw new ArgumentException($"Type '{eventType.FullName}' is not an agent struct event type.", nameof(eventType));

        TypeNames[eventType] = discriminator;
        DiscriminatorToType[discriminator] = eventType;
        if (typeInfo is not null)
            TypeInfos[eventType] = typeInfo;
    }

    /// <summary>
    /// Registers an agent struct event type with a discriminator and source-generated JSON metadata.
    /// </summary>
    public static void RegisterEventType<TEvent>(string discriminator, JsonTypeInfo<TEvent> typeInfo)
        where TEvent : struct, AgentStructEvent
    {
        RegisterEventType(typeof(TEvent), discriminator, typeInfo);
    }

    /// <summary>
    /// Deserializes an agent struct event wire envelope from JSON.
    /// </summary>
    public static object? FromJson(string json)
    {
        try
        {
            return DeserializeEnvelope(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deserializes an agent struct event wire envelope and throws when it is unknown.
    /// </summary>
    public static object DeserializeStructEventJson(string json) =>
        DeserializeEnvelope(json)
        ?? throw new JsonException("JSON payload is not a known agent struct event.");

    private static string ToJsonEnvelope(object value, Type concreteType, string version)
    {
        var eventType = GetEventTypeName(concreteType);
        var eventJson = JsonSerializer.Serialize(value, GetTypeInfo(concreteType));
        var prefix = $"\"version\":\"{version}\",\"type\":\"{eventType}\"";

        return eventJson == "{}"
            ? $"{{{prefix}}}"
            : eventJson.Insert(1, prefix + ",");
    }

    private static object? DeserializeEnvelope(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("type", out var typeProp))
            return null;

        var discriminator = typeProp.GetString();
        if (discriminator == null || !DiscriminatorToType.TryGetValue(discriminator, out var concreteType))
            return null;

        var typeInfo = GetTypeInfo(concreteType);
        using var payload = StripEnvelopeFields(doc.RootElement, typeInfo);
        return payload.RootElement.Deserialize(typeInfo);
    }

    private static JsonDocument StripEnvelopeFields(JsonElement root, JsonTypeInfo typeInfo)
    {
        var knownProperties = typeInfo.Properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.NameEquals("version") || property.NameEquals("type"))
                    continue;

                if (!knownProperties.Contains(property.Name))
                    continue;

                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        return JsonDocument.Parse(stream.ToArray());
    }

    private static JsonTypeInfo GetTypeInfo(Type concreteType)
    {
        if (TypeInfos.TryGetValue(concreteType, out var typeInfo))
            return typeInfo;

        typeInfo = AgentEventSerializer.StandardJsonOptions.TypeInfoResolver?.GetTypeInfo(
                concreteType,
                AgentEventSerializer.StandardJsonOptions)
            ?? throw new JsonException($"No JSON metadata registered for agent struct event type '{concreteType.FullName}'.");

        TypeInfos[concreteType] = typeInfo;
        return typeInfo;
    }

    private static string ToScreamingSnakeCase(string pascalCase)
    {
        if (pascalCase.EndsWith("Event", StringComparison.Ordinal))
            pascalCase = pascalCase[..^5];

        return PascalCaseToSnakeCaseRegex().Replace(pascalCase, "$1_$2").ToUpperInvariant();
    }

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex PascalCaseToSnakeCaseRegex();
}
