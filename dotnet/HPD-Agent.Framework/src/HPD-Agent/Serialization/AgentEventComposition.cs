using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Agent.Serialization;

/// <summary>Declares whether an agent event may enter the canonical thread journal.</summary>
public enum AgentEventDurability
{
    /// <summary>The event may be observed but may not be persisted in a thread journal.</summary>
    LiveOnly,

    /// <summary>The event is part of the stable durable replay contract.</summary>
    Durable
}

/// <summary>Complete reflection-free serialization metadata for one agent event contract.</summary>
public sealed record AgentEventDescriptor
{
    /// <summary>Gets the stable wire discriminator.</summary>
    public required string Discriminator { get; init; }

    /// <summary>Gets the concrete CLR event type.</summary>
    public required Type EventType { get; init; }

    /// <summary>Gets the source-generated JSON metadata for <see cref="EventType"/>.</summary>
    public required JsonTypeInfo JsonTypeInfo { get; init; }

    /// <summary>Gets the event's journal admission classification.</summary>
    public required AgentEventDurability Durability { get; init; }

    /// <summary>Gets the stable identity of the module that owns the event.</summary>
    public required string ModuleId { get; init; }
}

/// <summary>Immutable event declarations owned by one assembly or capability module.</summary>
public sealed record AgentEventModuleFragment
{
    /// <summary>Gets the stable reverse-domain-style module identity.</summary>
    public required string ModuleId { get; init; }

    /// <summary>Gets the events declared by the module.</summary>
    public required IReadOnlyList<AgentEventDescriptor> Events { get; init; }
}

/// <summary>Assembly metadata used by the application generator to close referenced event modules.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class HpdAgentEventModuleManifestAttribute(
    string moduleId,
    Type fragmentProviderType,
    params Type[] dependencyFragmentProviderTypes) : Attribute
{
    /// <summary>Gets the module identity.</summary>
    public string ModuleId { get; } = moduleId;

    /// <summary>Gets the generated public fragment provider.</summary>
    public Type FragmentProviderType { get; } = fragmentProviderType;

    /// <summary>Gets the explicit transitive fragment dependencies.</summary>
    public IReadOnlyList<Type> DependencyFragmentProviderTypes { get; } = dependencyFragmentProviderTypes;
}

/// <summary>Identifies an event-owning assembly and its source-generated JSON context.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class HpdAgentEventModuleAttribute(string moduleId, Type jsonSerializerContextType) : Attribute
{
    /// <summary>Gets the stable module identity.</summary>
    public string ModuleId { get; } = moduleId;

    /// <summary>Gets the user-authored source-generated JSON context type.</summary>
    public Type JsonSerializerContextType { get; } = jsonSerializerContextType;
}

/// <summary>Marks the final compilation that owns a generated closed application event composition.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class HpdAgentApplicationAttribute : Attribute;

/// <summary>Safe remotely consumable description of one event contract.</summary>
public sealed record AgentEventCatalogEntry(
    string Discriminator,
    string ContractName,
    AgentEventDurability Durability,
    string ModuleId);

/// <summary>Deterministic public catalog for an immutable event composition.</summary>
public sealed record AgentEventCatalog
{
    /// <summary>Gets event entries in canonical order.</summary>
    public required IReadOnlyList<AgentEventCatalogEntry> Events { get; init; }

    /// <summary>Gets the deterministic compatibility digest.</summary>
    public required string Digest { get; init; }
}

/// <summary>Thrown when a codec cannot resolve an event wire discriminator.</summary>
public sealed class UnknownAgentEventDiscriminatorException : JsonException
{
    /// <summary>Creates a safe unknown-discriminator failure.</summary>
    public UnknownAgentEventDiscriminatorException(string discriminator, string codecDigest)
        : base($"Agent event discriminator '{discriminator}' is not present in codec '{codecDigest}'.")
    {
        Discriminator = discriminator;
        CodecDigest = codecDigest;
    }

    /// <summary>Gets the unknown wire discriminator.</summary>
    public string Discriminator { get; }

    /// <summary>Gets the digest of the codec that rejected it.</summary>
    public string CodecDigest { get; }
}

/// <summary>Reports an unknown persisted event with safe journal coordinates and no payload data.</summary>
public sealed class UnknownDurableAgentEventException : Exception
{
    /// <summary>Creates a safe durable hydration failure.</summary>
    public UnknownDurableAgentEventException(
        string discriminator,
        string sessionId,
        string threadId,
        long journalGeneration,
        long sequenceNumber,
        string codecDigest,
        Exception innerException)
        : base($"Unknown durable agent event '{discriminator}' at session '{sessionId}', thread '{threadId}', generation {journalGeneration}, sequence {sequenceNumber}; codec '{codecDigest}'.", innerException)
    {
        Discriminator = discriminator;
        SessionId = sessionId;
        ThreadId = threadId;
        JournalGeneration = journalGeneration;
        SequenceNumber = sequenceNumber;
        CodecDigest = codecDigest;
    }

    /// <summary>Gets the rejected discriminator.</summary>
    public string Discriminator { get; }

    /// <summary>Gets the owning session ID.</summary>
    public string SessionId { get; }

    /// <summary>Gets the owning thread ID.</summary>
    public string ThreadId { get; }

    /// <summary>Gets the journal generation being hydrated.</summary>
    public long JournalGeneration { get; }

    /// <summary>Gets the persisted sequence number, or zero when unavailable.</summary>
    public long SequenceNumber { get; }

    /// <summary>Gets the rejecting codec digest.</summary>
    public string CodecDigest { get; }
}

/// <summary>Thrown when a live-only event is submitted to a durable journal operation.</summary>
public sealed class LiveOnlyAgentEventAppendException : InvalidOperationException
{
    /// <summary>Creates a live-only journal-admission failure.</summary>
    public LiveOnlyAgentEventAppendException(AgentEventDescriptor descriptor)
        : base($"Agent event '{descriptor.Discriminator}' is live-only and cannot enter a durable journal.")
    {
        Discriminator = descriptor.Discriminator;
        EventType = descriptor.EventType;
    }

    /// <summary>Gets the rejected discriminator.</summary>
    public string Discriminator { get; }

    /// <summary>Gets the rejected event type.</summary>
    public Type EventType { get; }
}

/// <summary>The sole output-event serialization authority for one immutable application composition.</summary>
public sealed class AgentEventCodec
{
    private readonly IReadOnlyDictionary<Type, AgentEventDescriptor> _byType;
    private readonly IReadOnlyDictionary<string, AgentEventDescriptor> _byDiscriminator;

    internal AgentEventCodec(
        IReadOnlyDictionary<Type, AgentEventDescriptor> byType,
        IReadOnlyDictionary<string, AgentEventDescriptor> byDiscriminator,
        AgentEventCatalog catalog)
    {
        _byType = byType;
        _byDiscriminator = byDiscriminator;
        Catalog = catalog;
    }

    /// <summary>Gets the immutable event catalog.</summary>
    public AgentEventCatalog Catalog { get; }

    /// <summary>Gets the composition compatibility digest.</summary>
    public string Digest => Catalog.Digest;

    /// <summary>Serializes an event using its exact generated metadata and stable flat envelope.</summary>
    public string Serialize(AgentEvent value, string version = "1.0")
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        if (!_byType.TryGetValue(value.GetType(), out var descriptor))
            throw new JsonException($"Agent event type '{value.GetType().FullName}' is not present in codec '{Digest}'.");

        var eventJson = JsonSerializer.Serialize(value, descriptor.JsonTypeInfo);
        var prefix = $"\"version\":{JsonSerializer.Serialize(version, AgentEventJsonContext.Default.String)}," +
            $"\"type\":{JsonSerializer.Serialize(descriptor.Discriminator, AgentEventJsonContext.Default.String)}";
        if (value is IErrorEvent errorEvent)
        {
            prefix += ",\"isError\":true";
            if (!eventJson.Contains("\"errorMessage\"", StringComparison.Ordinal))
                prefix += $",\"errorMessage\":{JsonSerializer.Serialize(errorEvent.ErrorMessage, AgentEventJsonContext.Default.String)}";
        }

        return eventJson == "{}"
            ? $"{{{prefix}}}"
            : eventJson.Insert(1, prefix + ",");
    }

    /// <summary>Hydrates an event using its discriminator's exact generated metadata.</summary>
    public AgentEvent DeserializeEvent(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("type", out var typeProperty) ||
            typeProperty.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Agent event envelope requires a string 'type' property.");
        }

        var discriminator = typeProperty.GetString()!;
        if (!_byDiscriminator.TryGetValue(discriminator, out var descriptor))
            throw new UnknownAgentEventDiscriminatorException(discriminator, Digest);

        using var payload = StripEnvelope(document.RootElement, descriptor.JsonTypeInfo);
        return payload.RootElement.Deserialize(descriptor.JsonTypeInfo) as AgentEvent
            ?? throw new JsonException($"Agent event '{discriminator}' hydrated to an invalid value.");
    }

    /// <summary>Looks up a descriptor by concrete event type.</summary>
    public bool TryGetByType(Type eventType, out AgentEventDescriptor descriptor) =>
        _byType.TryGetValue(eventType, out descriptor!);

    /// <summary>Looks up a descriptor by stable wire discriminator.</summary>
    public bool TryGetByDiscriminator(string discriminator, out AgentEventDescriptor descriptor) =>
        _byDiscriminator.TryGetValue(discriminator, out descriptor!);

    /// <summary>Requires that an event is registered and explicitly durable.</summary>
    public AgentEventDescriptor RequireDurable(AgentEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!_byType.TryGetValue(value.GetType(), out var descriptor))
            throw new JsonException($"Agent event type '{value.GetType().FullName}' is not present in codec '{Digest}'.");
        if (descriptor.Durability != AgentEventDurability.Durable)
            throw new LiveOnlyAgentEventAppendException(descriptor);
        return descriptor;
    }

    private static JsonDocument StripEnvelope(JsonElement root, JsonTypeInfo typeInfo)
    {
        var knownProperties = typeInfo.Properties.Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.NameEquals("version") || property.NameEquals("type"))
                    continue;
                if (knownProperties.Contains(property.Name))
                    property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return JsonDocument.Parse(stream.ToArray());
    }
}

/// <summary>Closed immutable event authority for one application.</summary>
public sealed class AgentEventComposition
{
    private const string WireFormatVersion = "1.0";

    private AgentEventComposition(
        IReadOnlyList<AgentEventModuleFragment> fragments,
        AgentEventCatalog catalog,
        IReadOnlyDictionary<Type, AgentEventDescriptor> byType,
        IReadOnlyDictionary<string, AgentEventDescriptor> byDiscriminator)
    {
        Fragments = fragments;
        Catalog = catalog;
        Codec = new AgentEventCodec(byType, byDiscriminator, catalog);
    }

    /// <summary>Gets the exact immutable module fragments in canonical order.</summary>
    public IReadOnlyList<AgentEventModuleFragment> Fragments { get; }

    /// <summary>Gets the sole codec built from this composition.</summary>
    public AgentEventCodec Codec { get; }

    /// <summary>Gets the safe deterministic event catalog.</summary>
    public AgentEventCatalog Catalog { get; }

    /// <summary>Gets the catalog compatibility digest.</summary>
    public string Digest => Catalog.Digest;

    /// <summary>Validates and freezes a complete application event graph.</summary>
    public static AgentEventComposition Create(IReadOnlyList<AgentEventModuleFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        var orderedFragments = fragments
            .Select(static fragment => fragment ?? throw new InvalidOperationException("An agent event module fragment cannot be null."))
            .Select(static fragment => new AgentEventModuleFragment
            {
                ModuleId = fragment.ModuleId,
                Events = Array.AsReadOnly((fragment.Events ?? throw new InvalidOperationException(
                    $"Agent event module '{fragment.ModuleId}' has no event descriptor collection.")).ToArray())
            })
            .OrderBy(static fragment => fragment.ModuleId, StringComparer.Ordinal)
            .ToArray();
        var modules = new Dictionary<string, AgentEventModuleFragment>(StringComparer.Ordinal);
        var byType = new Dictionary<Type, AgentEventDescriptor>();
        var byDiscriminator = new Dictionary<string, AgentEventDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var fragment in orderedFragments)
        {
            ValidateModuleId(fragment.ModuleId);
            if (modules.TryGetValue(fragment.ModuleId, out var existing))
            {
                if (AreEquivalent(existing, fragment))
                    continue;
                throw new InvalidOperationException($"Agent event module ID '{fragment.ModuleId}' is claimed by non-identical fragments.");
            }
            modules.Add(fragment.ModuleId, fragment);

            foreach (var descriptor in fragment.Events.OrderBy(static value => value.Discriminator, StringComparer.Ordinal))
            {
                ValidateDescriptor(fragment, descriptor);
                if (byType.TryGetValue(descriptor.EventType, out var typeConflict))
                    throw new InvalidOperationException($"Agent event type '{descriptor.EventType.FullName}' claims both '{typeConflict.Discriminator}' and '{descriptor.Discriminator}'.");
                if (byDiscriminator.TryGetValue(descriptor.Discriminator, out var discriminatorConflict))
                    throw new InvalidOperationException($"Agent event discriminator '{descriptor.Discriminator}' is claimed by both '{discriminatorConflict.EventType.FullName}' and '{descriptor.EventType.FullName}'.");
                byType.Add(descriptor.EventType, descriptor);
                byDiscriminator.Add(descriptor.Discriminator, descriptor);
            }
        }

        var entries = byDiscriminator.Values
            .OrderBy(static descriptor => descriptor.ModuleId, StringComparer.Ordinal)
            .ThenBy(static descriptor => descriptor.Discriminator, StringComparer.Ordinal)
            .Select(static descriptor => new AgentEventCatalogEntry(
                descriptor.Discriminator,
                descriptor.EventType.FullName ?? descriptor.EventType.Name,
                descriptor.Durability,
                descriptor.ModuleId))
            .ToArray();
        var digest = ComputeDigest(entries);
        var catalog = new AgentEventCatalog
        {
            Events = Array.AsReadOnly(entries),
            Digest = digest
        };

        return new AgentEventComposition(
            Array.AsReadOnly(modules.Values.OrderBy(static fragment => fragment.ModuleId, StringComparer.Ordinal).ToArray()),
            catalog,
            new ReadOnlyDictionary<Type, AgentEventDescriptor>(byType),
            new ReadOnlyDictionary<string, AgentEventDescriptor>(byDiscriminator));
    }

    private static void ValidateDescriptor(AgentEventModuleFragment fragment, AgentEventDescriptor descriptor)
    {
        if (descriptor is null)
            throw new InvalidOperationException($"Agent event module '{fragment.ModuleId}' contains a null descriptor.");
        if (!StringComparer.Ordinal.Equals(fragment.ModuleId, descriptor.ModuleId))
            throw new InvalidOperationException($"Event '{descriptor.Discriminator}' declares module '{descriptor.ModuleId}' but is contained by '{fragment.ModuleId}'.");
        if (!typeof(AgentEvent).IsAssignableFrom(descriptor.EventType) || descriptor.EventType.IsAbstract || descriptor.EventType.ContainsGenericParameters)
            throw new InvalidOperationException($"Event type '{descriptor.EventType.FullName}' must be a closed concrete AgentEvent.");
        if (descriptor.JsonTypeInfo is null)
            throw new InvalidOperationException($"Event '{descriptor.EventType.FullName}' has no source-generated JSON metadata.");
        if (descriptor.JsonTypeInfo.Type != descriptor.EventType)
            throw new InvalidOperationException($"JSON metadata for '{descriptor.JsonTypeInfo.Type.FullName}' cannot describe '{descriptor.EventType.FullName}'.");
        if (descriptor.JsonTypeInfo.Properties.Any(static property => property.Name is "version" or "type"))
            throw new InvalidOperationException($"Event '{descriptor.EventType.FullName}' declares a reserved envelope property.");
        if (string.IsNullOrWhiteSpace(descriptor.Discriminator) ||
            descriptor.Discriminator.Any(static value => !(char.IsAsciiDigit(value) || char.IsAsciiLetterUpper(value) || value == '_')))
            throw new InvalidOperationException($"Event discriminator '{descriptor.Discriminator}' is not canonical SCREAMING_SNAKE_CASE.");
    }

    private static bool AreEquivalent(AgentEventModuleFragment left, AgentEventModuleFragment right)
    {
        if (left.Events.Count != right.Events.Count)
            return false;
        var leftEvents = left.Events.OrderBy(static descriptor => descriptor.Discriminator, StringComparer.Ordinal).ToArray();
        var rightEvents = right.Events.OrderBy(static descriptor => descriptor.Discriminator, StringComparer.Ordinal).ToArray();
        return leftEvents.Zip(rightEvents).All(static pair =>
            StringComparer.Ordinal.Equals(pair.First.Discriminator, pair.Second.Discriminator) &&
            pair.First.EventType == pair.Second.EventType &&
            pair.First.JsonTypeInfo?.Type == pair.Second.JsonTypeInfo?.Type &&
            pair.First.Durability == pair.Second.Durability &&
            StringComparer.Ordinal.Equals(pair.First.ModuleId, pair.Second.ModuleId));
    }

    private static void ValidateModuleId(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId) ||
            moduleId.Any(static value => !(char.IsAsciiLetterOrDigit(value) || value is '.' or '-' or '_')))
            throw new InvalidOperationException($"Agent event module ID '{moduleId}' is invalid.");
    }

    private static string ComputeDigest(IReadOnlyList<AgentEventCatalogEntry> entries)
    {
        var canonical = new StringBuilder(WireFormatVersion);
        foreach (var entry in entries)
            canonical.Append('\n').Append(entry.ModuleId).Append('|').Append(entry.Discriminator).Append('|')
                .Append(entry.ContractName).Append('|').Append(entry.Durability);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }
}

/// <summary>Keyed convenience publication for complete generated application compositions.</summary>
public static class AgentEventCompositionHost
{
    private static readonly ConcurrentDictionary<string, AgentEventComposition> Applications =
        new(StringComparer.Ordinal);

    /// <summary>Publishes one already-built application composition under its assembly identity.</summary>
    public static void RegisterApplication(AgentEventComposition composition, string applicationAssemblyIdentity)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationAssemblyIdentity);
        var registered = Applications.GetOrAdd(applicationAssemblyIdentity, composition);
        if (!StringComparer.Ordinal.Equals(registered.Digest, composition.Digest))
            throw new InvalidOperationException($"Application '{applicationAssemblyIdentity}' attempted to publish conflicting agent event compositions '{registered.Digest}' and '{composition.Digest}'.");
    }

    /// <summary>Gets the composition published by one application identity.</summary>
    public static bool TryGetApplication(string applicationAssemblyIdentity, out AgentEventComposition composition) =>
        Applications.TryGetValue(applicationAssemblyIdentity, out composition!);

    /// <summary>Gets the currently published application identities for standalone ambiguity diagnostics.</summary>
    public static IReadOnlyList<string> GetApplicationIdentities() =>
        Applications.Keys.OrderBy(static identity => identity, StringComparer.Ordinal).ToArray();
}
