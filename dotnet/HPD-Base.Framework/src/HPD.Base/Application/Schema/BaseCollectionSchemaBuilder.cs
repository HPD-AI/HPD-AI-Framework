using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;
/// <summary>Builds one validated typed collection and its canonical schema.</summary>
public sealed class BaseCollectionSchemaBuilder<T>
{
    private const int MaximumFields = 512;
    private const int MaximumIndexes = 128;
    private readonly string _id;
    private readonly JsonTypeInfo<T> _jsonTypeInfo;
    private readonly Dictionary<string, FieldEntry> _fields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IndexEntry> _indexes = new(StringComparer.Ordinal);
    private SchemaMode _schemaMode = SchemaMode.Strict;
    private UnknownFieldPolicy _unknownFields = UnknownFieldPolicy.Reject;
    private BaseCollectionMutationMode _mutationMode = BaseCollectionMutationMode.Mutable;
    private bool _system;
    private string? _systemOwnerModuleId;
    private readonly Dictionary<string, BaseStorageProtectionRequirement> _storageRequirements = new(StringComparer.Ordinal);
    internal BaseCollectionSchemaBuilder(string id, JsonTypeInfo<T> jsonTypeInfo)
    {
        BaseApplicationId.Validate(id, nameof(id));
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        if (jsonTypeInfo.Type != typeof(T))
        {
            throw new ArgumentException("JSON metadata describes a different record type.", nameof(jsonTypeInfo));
        }

        _id = id;
        _jsonTypeInfo = jsonTypeInfo;
    }

    /// <summary>Rejects undeclared fields. This is the default.</summary>
    public BaseCollectionSchemaBuilder<T> StrictDocument()
    {
        _schemaMode = SchemaMode.Strict;
        _unknownFields = UnknownFieldPolicy.Reject;
        return this;
    }

    /// <summary>Preserves undeclared fields. This must be selected explicitly.</summary>
    public BaseCollectionSchemaBuilder<T> LooseDocument()
    {
        _schemaMode = SchemaMode.Loose;
        _unknownFields = UnknownFieldPolicy.Preserve;
        return this;
    }

    /// <summary>Marks the collection as read-only.</summary>
    public BaseCollectionSchemaBuilder<T> ReadOnly()
    {
        _mutationMode = BaseCollectionMutationMode.ReadOnly;
        return this;
    }

    /// <summary>Allows creates only, optionally with host administrative purge.</summary>
    public BaseCollectionSchemaBuilder<T> AppendOnly(bool allowAdministrativePurge = false)
    {
        _mutationMode = allowAdministrativePurge
            ? BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge
            : BaseCollectionMutationMode.AppendOnly;
        return this;
    }

    /// <summary>Marks the collection as internal system data.</summary>
    public BaseCollectionSchemaBuilder<T> SystemCollection(string owningModuleId)
    {
        BaseApplicationId.Validate(owningModuleId, nameof(owningModuleId));
        _system = true;
        _systemOwnerModuleId = new string(owningModuleId.AsSpan());
        return this;
    }

    /// <summary>Performs string.</summary>
    public BaseSchemaFieldBuilder<T, string> String(string fieldId, string applicationName, BaseJsonProperty<T, string> property) => Field(fieldId, applicationName, property, "string");
    /// <summary>Performs boolean.</summary>
    public BaseSchemaFieldBuilder<T, bool> Boolean(string fieldId, string applicationName, BaseJsonProperty<T, bool> property) => Field(fieldId, applicationName, property, "boolean");
    /// <summary>Performs integer.</summary>
    public BaseSchemaFieldBuilder<T, int> Integer(string fieldId, string applicationName, BaseJsonProperty<T, int> property) => Field(fieldId, applicationName, property, "integer");
    /// <summary>Performs long.</summary>
    public BaseSchemaFieldBuilder<T, long> Long(string fieldId, string applicationName, BaseJsonProperty<T, long> property) => Field(fieldId, applicationName, property, "integer");
    /// <summary>Performs number.</summary>
    public BaseSchemaFieldBuilder<T, double> Number(string fieldId, string applicationName, BaseJsonProperty<T, double> property) => Field(fieldId, applicationName, property, "number");
    /// <summary>Performs decimal.</summary>
    public BaseSchemaFieldBuilder<T, decimal> Decimal(string fieldId, string applicationName, BaseJsonProperty<T, decimal> property) => Field(fieldId, applicationName, property, "number", "decimal");
    /// <summary>Performs date Time.</summary>
    public BaseSchemaFieldBuilder<T, DateTimeOffset> DateTime(string fieldId, string applicationName, BaseJsonProperty<T, DateTimeOffset> property) => Field(fieldId, applicationName, property, "string", "date-time");
    /// <summary>Performs enum.</summary>
    public BaseSchemaFieldBuilder<T, TEnum> Enum<TEnum>(string fieldId, string applicationName, BaseJsonProperty<T, TEnum> property)
        where TEnum : struct, Enum => Field(fieldId, applicationName, property, "string", "enum");
    /// <summary>Performs array.</summary>
    public BaseSchemaFieldBuilder<T, TValue[]> Array<TValue>(string fieldId, string applicationName, BaseJsonProperty<T, TValue[]> property) => Field(fieldId, applicationName, property, "array");
    /// <summary>Performs object.</summary>
    public BaseSchemaFieldBuilder<T, TValue> Object<TValue>(string fieldId, string applicationName, BaseJsonProperty<T, TValue> property) => Field(fieldId, applicationName, property, "object");
    /// <summary>Performs file Reference.</summary>
    public BaseSchemaFieldBuilder<T, string> FileReference(string fieldId, string applicationName, BaseJsonProperty<T, string> property) => Field(fieldId, applicationName, property, "string", "file-reference");
    /// <summary>Declares a bounded immutable binary field.</summary>
    public BaseSchemaFieldBuilder<T, BaseBinary> Binary(string fieldId, string applicationName, BaseJsonProperty<T, BaseBinary> property) => Field(fieldId, applicationName, property, "string", "base64");

    /// <summary>Requires storage protection for this collection from one owning module.</summary>
    public BaseCollectionSchemaBuilder<T> RequireStorageProtection(BaseStorageProtectionRequirement requirement)
    {
        BaseStorageProtectionContract.NormalizeRequirement(requirement);
        if (!_storageRequirements.TryAdd(requirement.OwningModuleId, BaseStorageProtectionContract.Clone(requirement)))
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageRequirementDuplicate);
        return this;
    }
    /// <summary>Declares a typed record-id relation using stable schema identities.</summary>
    public BaseRelationSchemaBuilder<T, TTarget> Relation<TTarget>(string relationId, string fieldId, string applicationName, BaseJsonProperty<T, BaseRecordId<TTarget>> property, BaseCollection<TTarget> target)
    {
        BaseApplicationId.Validate(relationId, nameof(relationId));
        ArgumentNullException.ThrowIfNull(target);
        BaseRecordIdJsonConverterFactory.Register<TTarget>();
        if (_fields.Values.Any(field => string.Equals(field.Relation?.Id, relationId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Relation '{relationId}' is already declared.");
        }

        Field(fieldId, applicationName, property, "string", "record-id");
        FieldEntry entry = _fields[fieldId];
        entry.Relation = new RelationDefinition
        {
            Id = relationId,
            SourceCollectionId = _id,
            SourceFieldId = fieldId,
            TargetCollectionId = target.Id,
            TargetFieldId = "base.recordId",
            Include = new RelationIncludeDefinition(),
        };
        return new BaseRelationSchemaBuilder<T, TTarget>(entry, manyValued: false);
    }

    /// <summary>Declares an ordered many-valued typed record-id relation using stable schema identities.</summary>
    public BaseRelationSchemaBuilder<T, TTarget> ManyRelation<TTarget>(string relationId, string fieldId, string applicationName, BaseJsonProperty<T, BaseRecordId<TTarget>[]> property, BaseCollection<TTarget> target)
    {
        BaseApplicationId.Validate(relationId, nameof(relationId));
        ArgumentNullException.ThrowIfNull(target);
        BaseRecordIdJsonConverterFactory.Register<TTarget>();
        if (_fields.Values.Any(field => string.Equals(field.Relation?.Id, relationId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Relation '{relationId}' is already declared.");
        Field(fieldId, applicationName, property, "array", "record-id");
        FieldEntry entry = _fields[fieldId];
        entry.IsRequired = true;
        entry.IsNullable = false;
        entry.Relation = new RelationDefinition
        {
            Id = relationId,
            SourceCollectionId = _id,
            SourceFieldId = fieldId,
            TargetCollectionId = target.Id,
            TargetFieldId = "base.recordId",
            LocalMultiplicity = BaseRelationMultiplicity.Many,
            Include = new RelationIncludeDefinition(),
        };
        return new BaseRelationSchemaBuilder<T, TTarget>(entry, manyValued: true);
    }

    /// <summary>Declares a validated index over known stored fields.</summary>
    public BaseSchemaIndexBuilder<T> Index(string id, params ReadOnlySpan<string> fields)
    {
        BaseApplicationId.Validate(id, nameof(id));
        if (_indexes.Count >= MaximumIndexes)
        {
            throw new InvalidOperationException($"A collection may declare at most {MaximumIndexes} indexes.");
        }

        if (_indexes.ContainsKey(id))
        {
            throw new InvalidOperationException($"Index '{id}' is already declared.");
        }

        if (fields.Length == 0)
        {
            throw new ArgumentException("An index must contain at least one field.", nameof(fields));
        }

        string[] fieldIds = fields.ToArray();
        foreach (string fieldId in fieldIds)
        {
            if (!_fields.ContainsKey(fieldId))
            {
                throw new InvalidOperationException($"Index '{id}' references unknown field '{fieldId}'.");
            }
        }

        var entry = new IndexEntry(id, fieldIds);
        _indexes.Add(id, entry);
        return new BaseSchemaIndexBuilder<T>(entry);
    }

    /// <summary>Performs build.</summary>
    internal BaseCollection<T> Build()
    {
        FieldDefinition[] fields = _fields.Values.Select(static entry => entry.Definition()).ToArray();
        IndexDefinition[] indexes = _indexes.Values.Select(entry => entry.Definition(_id)).ToArray();
        var definition = new CollectionDefinition
        {
            Id = _id,
            Name = _id,
            Kind = "record",
            System = _system,
            SystemOwnerModuleId = _systemOwnerModuleId,
            Exposed = !_system,
            MutationMode = _mutationMode,
            SchemaMode = _schemaMode,
            UnknownFields = _unknownFields,
            Fields = fields,
            Indexes = indexes,
            Source = new SchemaSourceDescriptor
            {
                Id = "hpd.base.application.generated",
                Kind = SchemaSourceKind.Generated,
            },
            StorageProtectionRequirements = _storageRequirements.Count == 0 ? null : _storageRequirements.Values.Select(BaseStorageProtectionContract.Clone).ToArray(),
        };
        return BaseCollection<T>.Create(definition, _jsonTypeInfo, declarations =>
        {
            foreach (FieldEntry entry in _fields.Values)
            {
                entry.AddTo(declarations);
            }
        });
    }

    /// <summary>Performs field.</summary>
    private BaseSchemaFieldBuilder<T, TValue> Field<TValue>(string fieldId, string applicationName, BaseJsonProperty<T, TValue> property, string type, string? format = null)
    {
        BaseApplicationId.Validate(fieldId, nameof(fieldId));
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(property);
        if (!ReferenceEquals(property.Owner, _jsonTypeInfo))
            throw new InvalidOperationException("base.schema.serializer.metadataInvalid");
        if (_fields.Count >= MaximumFields)
        {
            throw new InvalidOperationException($"A collection may declare at most {MaximumFields} fields.");
        }

        if (_fields.ContainsKey(fieldId))
        {
            throw new InvalidOperationException($"Field '{fieldId}' is already declared.");
        }

        if (_fields.Values.Any(field => string.Equals(field.ApplicationName, applicationName, StringComparison.Ordinal)
            || string.Equals(field.WireName, property.WireName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("A field application or wire name is already declared.");
        }

        var entry = new FieldEntry<TValue>(fieldId, applicationName, property.WireName, type, format);
        _fields.Add(fieldId, entry);
        return new BaseSchemaFieldBuilder<T, TValue>(entry);
    }

internal abstract class FieldEntry
    {
        /// <summary>Initializes a new instance.</summary>
        protected FieldEntry(string id, string applicationName, string wireName, string type, string? format)
        {
            Id = id;
            ApplicationName = applicationName;
            WireName = wireName;
            Type = type;
            Format = format;
        }

        /// <summary>Gets id.</summary>
        protected string Id { get; }
        internal string ApplicationName { get; }
        internal string WireName { get; }
        /// <summary>Gets type.</summary>
        protected string Type { get; }
        /// <summary>Gets format.</summary>
        protected string? Format { get; }
        internal bool IsRequired { get; set; }
        internal bool IsNullable { get; set; } = true;
        internal RelationDefinition? Relation { get; set; }
        internal BaseFieldConfidentiality Confidentiality { get; set; } = BaseFieldConfidentiality.Public;
        internal BaseFieldDisclosurePolicy? Disclosure { get; set; }
        internal int? MaximumBytes { get; set; }
        internal bool ConfidentialityAssigned { get; set; }
        internal bool DisclosureAssigned { get; set; }

        /// <summary>Performs definition.</summary>
        internal abstract FieldDefinition Definition();
        /// <summary>Performs add To.</summary>
        internal abstract void AddTo(BaseCollectionFields<T> declarations);
    }

internal sealed class FieldEntry<TValue>(string id, string applicationName, string wireName, string type, string? format) : FieldEntry(id, applicationName, wireName, type, format)
    {
        /// <summary>Performs definition.</summary>
        internal override FieldDefinition Definition() => new()
        {
            Id = Id,
            ApplicationName = ApplicationName,
            WireName = WireName,
            Type = Type,
            Format = Format,
            Required = IsRequired,
            Nullable = IsNullable,
            Relation = Relation is null ? null : Relation with
            {
                Required = IsRequired
            },
            Confidentiality = Confidentiality,
            Disclosure = BaseConfidentialityPolicy.Normalize(Confidentiality, Disclosure),
            MaximumBytes = MaximumBytes,
        };
        /// <summary>Performs add To.</summary>
        internal override void AddTo(BaseCollectionFields<T> declarations) => declarations.Add<TValue>(Id, ApplicationName, WireName, IsNullable);
    }

internal sealed class IndexEntry(string id, string[] fields)
    {
        internal bool Required { get; set; }
        internal bool Unique { get; set; }

        /// <summary>Performs definition.</summary>
        internal IndexDefinition Definition(string collectionId) => new()
        {
            Id = id,
            Name = id,
            CollectionId = collectionId,
            Kind = Unique ? IndexKind.Unique : IndexKind.Key,
            Unique = Unique,
            Status = IndexStatus.Unknown,
            Enforcement = Required ? EnforcementOwner.Store : EnforcementOwner.Advisory,
            Parts = fields.Select(static field => new IndexPart { Kind = IndexPartKind.Field, FieldId = field, }).ToArray(),
        };
    }
}

/// <summary>Configures one canonical typed relation declaration.</summary>
public sealed class BaseRelationSchemaBuilder<TSource, TTarget>
{
    private readonly BaseCollectionSchemaBuilder<TSource>.FieldEntry _entry;
    private readonly bool _manyValued;
    internal BaseRelationSchemaBuilder(BaseCollectionSchemaBuilder<TSource>.FieldEntry entry, bool manyValued)
    {
        _entry = entry;
        _manyValued = manyValued;
    }

    /// <summary>Requires exactly one locally stored target reference.</summary>
    public BaseRelationSchemaBuilder<TSource, TTarget> ExactlyOne()
    {
        if (_manyValued)
            throw new InvalidOperationException("A many-valued relation cannot use singular cardinality.");
        _entry.IsRequired = true;
        _entry.IsNullable = false;
        _entry.Relation = _entry.Relation!with
        {
            LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne,
            Required = true,
        };
        return this;
    }

    /// <summary>Allows zero or one locally stored target reference.</summary>
    public BaseRelationSchemaBuilder<TSource, TTarget> ZeroOrOne()
    {
        if (_manyValued)
            throw new InvalidOperationException("A many-valued relation cannot use singular cardinality.");
        _entry.IsRequired = false;
        _entry.IsNullable = true;
        _entry.Relation = _entry.Relation!with
        {
            LocalMultiplicity = BaseRelationMultiplicity.ZeroOrOne,
            Required = false,
        };
        return this;
    }

    /// <summary>Configures an ordered many-valued relation with optional inclusive target-count bounds.</summary>
    public BaseRelationSchemaBuilder<TSource, TTarget> Many(int? minimumCount = null, int? maximumCount = null)
    {
        if (!_manyValued)
            throw new InvalidOperationException("Use ManyRelation to declare a many-valued record-id field.");
        if (minimumCount is < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumCount));
        if (maximumCount is < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (minimumCount > maximumCount)
            throw new ArgumentException("The minimum count cannot exceed the maximum count.", nameof(minimumCount));
        _entry.IsRequired = true;
        _entry.IsNullable = false;
        _entry.Relation = _entry.Relation!with
        {
            LocalMultiplicity = BaseRelationMultiplicity.Many,
            Required = true,
            MinimumCount = minimumCount,
            MaximumCount = maximumCount,
        };
        return this;
    }

    /// <summary>Declares the inverse navigation and its multiplicity.</summary>
    public BaseRelationSchemaBuilder<TSource, TTarget> Inverse(string navigationId, BaseRelationMultiplicity multiplicity = BaseRelationMultiplicity.Many)
    {
        BaseApplicationId.Validate(navigationId, nameof(navigationId));
        _entry.Relation = _entry.Relation!with
        {
            InverseNavigationId = navigationId,
            InverseMultiplicity = multiplicity,
        };
        return this;
    }

    /// <summary>Allows this navigation in record include trees.</summary>
    public BaseRelationSchemaBuilder<TSource, TTarget> Include(int? maximumDepth = null, bool allowFilter = false, bool allowSort = false)
    {
        if (maximumDepth is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumDepth.Value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumDepth.Value, 32);
        }

        _entry.Relation = _entry.Relation!with
        {
            Include = new RelationIncludeDefinition
            {
                Allowed = true,
                MaxDepth = maximumDepth,
                FilterAllowed = allowFilter,
                SortAllowed = allowSort,
            },
        };
        return this;
    }
}

/// <summary>Configures one field while preventing contradictory nullability.</summary>
public sealed class BaseSchemaFieldBuilder<TRecord, TValue>
{
    private readonly BaseCollectionSchemaBuilder<TRecord>.FieldEntry _entry;
    internal BaseSchemaFieldBuilder(BaseCollectionSchemaBuilder<TRecord>.FieldEntry entry) => _entry = entry;
    /// <summary>Performs required.</summary>
    public BaseSchemaFieldBuilder<TRecord, TValue> Required()
    {
        _entry.IsRequired = true;
        _entry.IsNullable = false;
        return this;
    }

    /// <summary>Performs optional.</summary>
    public BaseSchemaFieldBuilder<TRecord, TValue> Optional()
    {
        _entry.IsRequired = false;
        _entry.IsNullable = true;
        return this;
    }

    /// <summary>Assigns the field confidentiality class exactly once.</summary>
    public BaseSchemaFieldBuilder<TRecord, TValue> Confidentiality(BaseFieldConfidentiality confidentiality)
    {
        if (_entry.ConfidentialityAssigned || !Enum.IsDefined(confidentiality))
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.ContractInvalid);
        _entry.ConfidentialityAssigned = true;
        _entry.Confidentiality = confidentiality;
        return this;
    }

    /// <summary>Assigns a narrowing disclosure policy exactly once.</summary>
    public BaseSchemaFieldBuilder<TRecord, TValue> Disclosure(BaseFieldDisclosurePolicy disclosure)
    {
        ArgumentNullException.ThrowIfNull(disclosure);
        if (_entry.DisclosureAssigned)
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.ContractInvalid);
        _entry.DisclosureAssigned = true;
        _entry.Disclosure = BaseConfidentialityPolicy.Clone(disclosure);
        return this;
    }

    /// <summary>Sets the mandatory decoded byte limit for a binary field.</summary>
    public BaseSchemaFieldBuilder<TRecord, TValue> MaximumBytes(int maximumBytes)
    {
        if (typeof(TValue) != typeof(BaseBinary) || maximumBytes is < 1 or > 1_048_576 || _entry.MaximumBytes is not null)
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.ContractInvalid);
        _entry.MaximumBytes = maximumBytes;
        return this;
    }
}

/// <summary>Declares whether an index is a required physical capability or advisory.</summary>
public sealed class BaseSchemaIndexBuilder<T>
{
    private readonly BaseCollectionSchemaBuilder<T>.IndexEntry _entry;
    internal BaseSchemaIndexBuilder(BaseCollectionSchemaBuilder<T>.IndexEntry entry) => _entry = entry;
    /// <summary>Performs required.</summary>
    public BaseSchemaIndexBuilder<T> Required()
    {
        _entry.Required = true;
        return this;
    }

    /// <summary>Performs advisory.</summary>
    public BaseSchemaIndexBuilder<T> Advisory()
    {
        _entry.Required = false;
        return this;
    }

    /// <summary>Performs unique.</summary>
    public BaseSchemaIndexBuilder<T> Unique()
    {
        _entry.Unique = true;
        return this;
    }
}
