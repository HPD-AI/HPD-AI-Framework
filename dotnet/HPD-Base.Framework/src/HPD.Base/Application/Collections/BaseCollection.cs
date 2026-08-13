using System.Collections.ObjectModel;
using System.Text.Json.Serialization.Metadata;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;
/// <summary>
/// Represents an immutable, typed application contract for one BASE collection.
/// </summary>
/// <typeparam name = "T">The persisted record type.</typeparam>
public sealed class BaseCollection<T>
{
    /// <summary>Provides _fields.</summary>
    private readonly ReadOnlyDictionary<string, object> _fields;
    /// <summary>Provides _definition.</summary>
    private readonly CollectionDefinition _definition;
    private BaseCollection(CollectionDefinition definition, JsonTypeInfo<T> jsonTypeInfo, IReadOnlyDictionary<string, object> fields)
    {
        _definition = Snapshot(definition);
        JsonTypeInfo = jsonTypeInfo;
        _fields = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(fields, StringComparer.Ordinal));
    }

    /// <summary>
    /// Gets the canonical collection identifier.
    /// </summary>
    public string Id => _definition.Id;
    /// <summary>
    /// Gets the source-generated JSON contract for the persisted type.
    /// </summary>
    public JsonTypeInfo<T> JsonTypeInfo { get; }
    /// <summary>
    /// Gets the canonical immutable collection definition.
    /// </summary>
    public CollectionDefinition Definition => Snapshot(_definition);
    /// <summary>
    /// Gets the immutable generated or manually declared field set used by infrastructure.
    /// </summary>
    internal IReadOnlyDictionary<string, object> Fields => _fields;

    /// <summary>
    /// Creates a validated manual collection contract.
    /// </summary>
    /// <param name = "definition">The canonical collection definition.</param>
    /// <param name = "jsonTypeInfo">Source-generated JSON metadata for <typeparamref name = "T"/>.</param>
    /// <param name = "configure">The typed field declaration callback.</param>
    /// <returns>An immutable typed collection contract.</returns>
    public static BaseCollection<T> Create(CollectionDefinition definition, JsonTypeInfo<T> jsonTypeInfo, Action<BaseCollectionFields<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        if (jsonTypeInfo.Type != typeof(T))
        {
            throw new ArgumentException($"JSON metadata must describe '{typeof(T).FullName}'.", nameof(jsonTypeInfo));
        }

        var fields = new BaseCollectionFields<T>();
        configure(fields);
        fields.Seal();
        jsonTypeInfo.Options.MakeReadOnly();
        jsonTypeInfo.MakeReadOnly();
        CollectionDefinition installed = definition with
        {
            SerializerContractChecksum = SerializerChecksum(definition, jsonTypeInfo)
        };
        return new BaseCollection<T>(installed, jsonTypeInfo, fields.Items);
    }

    private static string SerializerChecksum(CollectionDefinition definition, JsonTypeInfo<T> metadata)
    {
        var text = new StringBuilder("hpd.base.serializer.v1\n")
            .Append(typeof(T).FullName).Append('\n');
        JsonPropertyInfo[] properties = metadata.Properties.OrderBy(static property => property.Name, StringComparer.Ordinal).ToArray();
        foreach (FieldDefinition field in (definition.Fields ?? []).OrderBy(static field => field.Id, StringComparer.Ordinal))
        {
            int ordinal = Array.FindIndex(properties, property => string.Equals(property.Name, field.WireName, StringComparison.Ordinal));
            if (ordinal < 0 && typeof(T) != typeof(System.Text.Json.JsonElement))
                throw new InvalidOperationException("base.schema.serializer.metadataInvalid");
            JsonPropertyInfo? property = ordinal < 0 ? null : properties[ordinal];
            text.Append(field.Id).Append('\n').Append(ordinal).Append('\n')
                .Append(field.ApplicationName).Append('\n').Append(field.WireName).Append('\n')
                .Append(property?.PropertyType.FullName ?? field.Type).Append('\n').Append(property?.Get is not null ? '1' : '0')
                .Append(property?.Set is not null ? '1' : '0').Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>Performs snapshot.</summary>
    private static CollectionDefinition Snapshot(CollectionDefinition definition) => definition with
    {
        Fields = definition.Fields?.Select(static field => field with { Disclosure = field.Disclosure is null ? null : BaseConfidentialityPolicy.Clone(field.Disclosure), RequiredCapabilities = field.RequiredCapabilities?.ToArray(), Extensions = field.Extensions is null ? null : new Dictionary<string, System.Text.Json.JsonElement>(field.Extensions, StringComparer.Ordinal), }).ToArray(),
        Indexes = definition.Indexes?.Select(static index => index with { Parts = index.Parts?.Select(static part => part with { Extensions = part.Extensions is null ? null : new Dictionary<string, System.Text.Json.JsonElement>(part.Extensions, StringComparer.Ordinal), }).ToArray(), Extensions = index.Extensions is null ? null : new Dictionary<string, System.Text.Json.JsonElement>(index.Extensions, StringComparer.Ordinal), }).ToArray(),
        VectorIndexes = definition.VectorIndexes?.Select(static index => index with { FilterFieldIds = index.FilterFieldIds.ToArray() }).ToArray(),
        PolicyRefs = definition.PolicyRefs?.ToArray(),
        RequiredCapabilities = definition.RequiredCapabilities?.ToArray(),
        Diagnostics = definition.Diagnostics?.ToArray(),
        Extensions = definition.Extensions is null ? null : new Dictionary<string, System.Text.Json.JsonElement>(definition.Extensions, StringComparer.Ordinal),
        StorageProtectionRequirements = definition.StorageProtectionRequirements?.Select(BaseStorageProtectionContract.Clone).ToArray(),
    };
}
