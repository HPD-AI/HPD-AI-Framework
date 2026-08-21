using System.Collections.ObjectModel;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;
/// <summary>
/// Represents an immutable, typed application contract for one BASE collection.
/// </summary>
/// <typeparam name = "T">The persisted record type.</typeparam>
public sealed class BaseCollection<T> : IBaseSerializerMetadataSource
{
    /// <summary>Provides _fields.</summary>
    private readonly ReadOnlyDictionary<string, object> _fields;
    /// <summary>Provides _definition.</summary>
    private CollectionDefinition _definition;
    private readonly JsonTypeInfo<T>? _jsonTypeInfo;
    private readonly IReadOnlyList<BaseSerializerPropertyDeclaration>? _serializerDeclarations;
    private BaseCollection(CollectionDefinition definition, JsonTypeInfo<T>? jsonTypeInfo, IReadOnlyDictionary<string, object> fields, BaseSerializerContextRegistration? registration, IReadOnlyList<BaseSerializerPropertyDeclaration>? serializerDeclarations = null)
    {
        _definition = Snapshot(definition);
        _jsonTypeInfo = jsonTypeInfo;
        _fields = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(fields, StringComparer.Ordinal));
        Registration = registration;
        _serializerDeclarations = serializerDeclarations;
    }

    /// <summary>
    /// Gets the canonical collection identifier.
    /// </summary>
    public string Id => _definition.Id;
    /// <summary>
    /// Gets the source-generated JSON contract for the persisted type.
    /// </summary>
    internal JsonTypeInfo<T> JsonTypeInfo => _jsonTypeInfo ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
    /// <summary>
    /// Gets the canonical immutable collection definition.
    /// </summary>
    public CollectionDefinition Definition => Snapshot(_definition);
    /// <summary>
    /// Gets the immutable generated or manually declared field set used by infrastructure.
    /// </summary>
    internal IReadOnlyDictionary<string, object> Fields => _fields;
    IReadOnlyList<JsonTypeInfo> IBaseSerializerMetadataSource.Roots => _jsonTypeInfo is null ? [] : [_jsonTypeInfo];
    bool IBaseSerializerMetadataSource.Generated => Registration is not null;
    BaseSerializerContextRegistration? IBaseSerializerMetadataSource.Registration => Registration;
    IReadOnlyList<Type> IBaseSerializerMetadataSource.RootTypes => [typeof(T)];
    IReadOnlyList<BaseSerializerPropertyDeclaration>? IBaseSerializerMetadataSource.SerializerDeclarations => _serializerDeclarations;
    private BaseSerializerContextRegistration? Registration { get; }
    void IBaseSerializerMetadataSource.Bind(BaseSerializerMetadataOwner owner)
    {
        if (Registration is null) return;
        JsonTypeInfo<T> metadata = owner.Resolve(this);
        string serializerChecksum = SerializerChecksum(_definition, metadata, _fields, _serializerDeclarations);
        _definition = BindTextIndexes(_definition with { SerializerContractChecksum = serializerChecksum }, serializerChecksum);
    }
    CollectionDefinition? IBaseSerializerMetadataSource.CollectionDefinition => Definition;

    /// <summary>
    /// Creates a validated manual collection contract.
    /// </summary>
    /// <param name = "definition">The canonical collection definition.</param>
    /// <param name = "jsonTypeInfo">Source-generated JSON metadata for <typeparamref name = "T"/>.</param>
    /// <param name = "configure">The typed field declaration callback.</param>
    /// <returns>An immutable typed collection contract.</returns>
    public static BaseCollection<T> Create(CollectionDefinition definition, JsonTypeInfo<T> jsonTypeInfo, Action<BaseCollectionFields<T>> configure) =>
        Create(definition, jsonTypeInfo, configure, null);

    /// <summary>Creates a generated collection from its closed serializer declaration.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseCollection<T> Create(
        CollectionDefinition definition,
        JsonTypeInfo<T> jsonTypeInfo,
        Action<BaseCollectionFields<T>> configure,
        IReadOnlyList<BaseSerializerPropertyDeclaration>? serializerDeclarations)
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
        string serializerChecksum = SerializerChecksum(definition, jsonTypeInfo, fields.Items, serializerDeclarations);
        CollectionDefinition installed = BindTextIndexes(definition with { SerializerContractChecksum = serializerChecksum }, serializerChecksum);
        return new BaseCollection<T>(installed, jsonTypeInfo, fields.Items, null);
    }

    /// <summary>Creates a generated collection from an opaque serializer registration.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseCollection<T> CreateGenerated(
        CollectionDefinition definition,
        BaseSerializerContextRegistration registration,
        Action<BaseCollectionFields<T>> configure,
        IReadOnlyList<BaseSerializerPropertyDeclaration> serializerDeclarations)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.AssertOwner(typeof(T));
        var fields = new BaseCollectionFields<T>();
        configure(fields);
        fields.Seal();
        CollectionDefinition installed = Snapshot(definition) with { SerializerContractChecksum = string.Empty };
        return new BaseCollection<T>(installed, null, fields.Items, registration, serializerDeclarations);
    }

    private static string SerializerChecksum(CollectionDefinition definition, JsonTypeInfo<T> metadata, IReadOnlyDictionary<string, object> fields, IReadOnlyList<BaseSerializerPropertyDeclaration>? declarations)
    {
        var bindings = (definition.Fields ?? []).Select(static field => (field.Id, field.ApplicationName, field.WireName)).ToArray();
        if (bindings.Length == 0)
            bindings = fields.Values.Cast<IBaseFieldContract>().Select(static field => (field.Id, field.ApplicationName, field.WireName)).ToArray();
        return BaseSerializerContract.Checksum(metadata, bindings, declarations);
    }

    private static CollectionDefinition BindTextIndexes(CollectionDefinition definition, string serializerChecksum)
    {
        if (definition.TextIndexes is not { Length: > 0 }) return definition;
        byte[] checksum = Convert.FromHexString(serializerChecksum);
        return definition with
        {
            TextIndexes = definition.TextIndexes.Select(index => BaseTextIndexContract.Seal(index with
            {
                SerializerGraphChecksum = System.Collections.Immutable.ImmutableArray.Create(checksum.ToArray()),
                DefinitionChecksum = [],
            })).ToArray(),
        };
    }

    internal BaseCollection<T> WithDefinition(CollectionDefinition definition) =>
        new(definition, _jsonTypeInfo, _fields, Registration, _serializerDeclarations);

    /// <summary>Performs snapshot.</summary>
    private static CollectionDefinition Snapshot(CollectionDefinition definition) => definition with
    {
        Fields = definition.Fields?.Select(static field => field with { Disclosure = field.Disclosure is null ? null : BaseConfidentialityPolicy.Clone(field.Disclosure), RequiredCapabilities = field.RequiredCapabilities?.ToArray(), Extensions = field.Extensions is null ? null : new Dictionary<string, System.Text.Json.JsonElement>(field.Extensions, StringComparer.Ordinal), }).ToArray(),
        Indexes = definition.Indexes?.Select(static index => index with { Parts = index.Parts?.Select(static part => part with { Extensions = part.Extensions is null ? null : new Dictionary<string, System.Text.Json.JsonElement>(part.Extensions, StringComparer.Ordinal), }).ToArray(), Extensions = index.Extensions is null ? null : new Dictionary<string, System.Text.Json.JsonElement>(index.Extensions, StringComparer.Ordinal), }).ToArray(),
        VectorIndexes = definition.VectorIndexes?.Select(static index => index with { FilterFieldIds = index.FilterFieldIds.ToArray() }).ToArray(),
        TextIndexes = definition.TextIndexes?.Select(BaseTextIndexContract.Seal).ToArray(),
        PolicyRefs = definition.PolicyRefs?.ToArray(),
        RequiredCapabilities = definition.RequiredCapabilities?.ToArray(),
        Diagnostics = definition.Diagnostics?.ToArray(),
        Extensions = definition.Extensions is null ? null : new Dictionary<string, System.Text.Json.JsonElement>(definition.Extensions, StringComparer.Ordinal),
        StorageProtectionRequirements = definition.StorageProtectionRequirements?.Select(BaseStorageProtectionContract.Clone).ToArray(),
    };
}
