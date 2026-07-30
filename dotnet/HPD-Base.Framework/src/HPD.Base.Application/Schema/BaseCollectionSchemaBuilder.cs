using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPD.Base.Application.Collections;
using HPD.Base.Schema;

namespace HPD.Base.Application.Schema;

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
    private bool _readOnly;
    private bool _system;

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
        _readOnly = true;
        return this;
    }

    /// <summary>Marks the collection as internal system data.</summary>
    public BaseCollectionSchemaBuilder<T> SystemCollection()
    {
        _system = true;
        return this;
    }

    public BaseSchemaFieldBuilder<T, string> String(string name) => Field<string>(name, "string");
    public BaseSchemaFieldBuilder<T, bool> Boolean(string name) => Field<bool>(name, "boolean");
    public BaseSchemaFieldBuilder<T, int> Integer(string name) => Field<int>(name, "integer");
    public BaseSchemaFieldBuilder<T, long> Long(string name) => Field<long>(name, "integer");
    public BaseSchemaFieldBuilder<T, double> Number(string name) => Field<double>(name, "number");
    public BaseSchemaFieldBuilder<T, decimal> Decimal(string name) => Field<decimal>(name, "number", "decimal");
    public BaseSchemaFieldBuilder<T, DateTimeOffset> DateTime(string name) => Field<DateTimeOffset>(name, "string", "date-time");
    public BaseSchemaFieldBuilder<T, TEnum> Enum<TEnum>(string name) where TEnum : struct, Enum =>
        Field<TEnum>(name, "string", "enum");
    public BaseSchemaFieldBuilder<T, TValue[]> Array<TValue>(string name) => Field<TValue[]>(name, "array");
    public BaseSchemaFieldBuilder<T, TValue> Object<TValue>(string name) => Field<TValue>(name, "object");
    public BaseSchemaFieldBuilder<T, string> Relation(string name) => Field<string>(name, "string", "record-id");
    public BaseSchemaFieldBuilder<T, string> FileReference(string name) => Field<string>(name, "string", "file-reference");

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

        string[] paths = fields.ToArray();
        foreach (string path in paths)
        {
            if (!_fields.ContainsKey(path))
            {
                throw new InvalidOperationException($"Index '{id}' references unknown field '{path}'.");
            }
        }

        var entry = new IndexEntry(id, paths);
        _indexes.Add(id, entry);
        return new BaseSchemaIndexBuilder<T>(entry);
    }

    internal BaseCollection<T> Build()
    {
        FieldDefinition[] fields = _fields.Values.Select(static entry => entry.Definition()).ToArray();
        IndexDefinition[] indexes = _indexes.Values.Select(entry => entry.Definition(_id)).ToArray();
        var definition = new CollectionDefinition
        {
            Id = _id,
            Name = _id,
            Kind = "document",
            System = _system,
            Exposed = !_system,
            ReadOnly = _readOnly,
            Operations = _readOnly
                ? new CollectionOperationMatrix { List = true, Get = true }
                : new CollectionOperationMatrix
                {
                    List = true,
                    Get = true,
                    Create = true,
                    Patch = true,
                    Replace = true,
                    Upsert = true,
                    Delete = true,
                },
            SchemaMode = _schemaMode,
            UnknownFields = _unknownFields,
            Fields = fields,
            Indexes = indexes,
            Source = new SchemaSourceDescriptor
            {
                Id = "hpd.base.application",
                Kind = SchemaSourceKind.Generated,
            },
        };

        return BaseCollection<T>.Create(
            definition,
            _jsonTypeInfo,
            declarations =>
            {
                foreach (FieldEntry entry in _fields.Values)
                {
                    entry.AddTo(declarations);
                }
            });
    }

    private BaseSchemaFieldBuilder<T, TValue> Field<TValue>(
        string name,
        string type,
        string? format = null)
    {
        BaseApplicationId.Validate(name, nameof(name));
        if (_fields.Count >= MaximumFields)
        {
            throw new InvalidOperationException($"A collection may declare at most {MaximumFields} fields.");
        }

        if (_fields.ContainsKey(name))
        {
            throw new InvalidOperationException($"Field '{name}' is already declared.");
        }

        var entry = new FieldEntry<TValue>(name, type, format);
        _fields.Add(name, entry);
        return new BaseSchemaFieldBuilder<T, TValue>(entry);
    }

    internal abstract class FieldEntry
    {
        protected FieldEntry(string name, string type, string? format)
        {
            Name = name;
            Type = type;
            Format = format;
        }

        protected string Name { get; }
        protected string Type { get; }
        protected string? Format { get; }
        internal bool IsRequired { get; set; }
        internal bool IsNullable { get; set; } = true;
        internal abstract FieldDefinition Definition();
        internal abstract void AddTo(BaseCollectionFields<T> declarations);
    }

    internal sealed class FieldEntry<TValue>(string name, string type, string? format)
        : FieldEntry(name, type, format)
    {
        internal override FieldDefinition Definition() => new()
        {
            Id = Name,
            Name = Name,
            Type = Type,
            Format = Format,
            Required = IsRequired,
            Nullable = IsNullable,
        };

        internal override void AddTo(BaseCollectionFields<T> declarations) =>
            declarations.Add<TValue>(Name, IsNullable);
    }

    internal sealed class IndexEntry(string id, string[] fields)
    {
        internal bool Required { get; set; }
        internal bool Unique { get; set; }

        internal IndexDefinition Definition(string collectionId) => new()
        {
            Id = id,
            Name = id,
            CollectionId = collectionId,
            Kind = Unique ? IndexKind.Unique : IndexKind.Key,
            Unique = Unique,
            Status = IndexStatus.Unknown,
            Enforcement = Required ? EnforcementOwner.Store : EnforcementOwner.Advisory,
            Parts = fields.Select(static field => new IndexPart
            {
                Kind = IndexPartKind.Field,
                FieldPath = field,
            }).ToArray(),
            Extensions = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["hpd.base.requiredPhysical"] = BooleanElement(Required),
            },
        };

        private static JsonElement BooleanElement(bool value)
        {
            using JsonDocument document = JsonDocument.Parse(value ? "true" : "false");
            return document.RootElement.Clone();
        }
    }
}

/// <summary>Configures one field while preventing contradictory nullability.</summary>
public sealed class BaseSchemaFieldBuilder<TRecord, TValue>
{
    private readonly BaseCollectionSchemaBuilder<TRecord>.FieldEntry _entry;

    internal BaseSchemaFieldBuilder(BaseCollectionSchemaBuilder<TRecord>.FieldEntry entry) =>
        _entry = entry;

    public BaseSchemaFieldBuilder<TRecord, TValue> Required()
    {
        _entry.IsRequired = true;
        _entry.IsNullable = false;
        return this;
    }

    public BaseSchemaFieldBuilder<TRecord, TValue> Optional()
    {
        _entry.IsRequired = false;
        _entry.IsNullable = true;
        return this;
    }
}

/// <summary>Declares whether an index is a required physical capability or advisory.</summary>
public sealed class BaseSchemaIndexBuilder<T>
{
    private readonly BaseCollectionSchemaBuilder<T>.IndexEntry _entry;

    internal BaseSchemaIndexBuilder(BaseCollectionSchemaBuilder<T>.IndexEntry entry) =>
        _entry = entry;

    public BaseSchemaIndexBuilder<T> Required()
    {
        _entry.Required = true;
        return this;
    }

    public BaseSchemaIndexBuilder<T> Advisory()
    {
        _entry.Required = false;
        return this;
    }

    public BaseSchemaIndexBuilder<T> Unique()
    {
        _entry.Unique = true;
        return this;
    }
}
