using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

#pragma warning disable CS1591 // XML documentation is completed before the contract checkpoint closes.

namespace HPD.Base;

/// <summary>Builds one closed scalar-constraint set for a typed field.</summary>
public sealed class BaseScalarConstraintBuilder<TValue>
{
    private BaseScalarConstraintSet _value = new();
    public BaseScalarConstraintBuilder<TValue> Utf8Bytes(int? minimum, int? maximum) { _value = _value with { MinimumUtf8Bytes = minimum, MaximumUtf8Bytes = maximum }; return this; }
    public BaseScalarConstraintBuilder<TValue> RequireNfc() { _value = _value with { StringNormalization = BaseStringNormalizationRequirement.RequireNfc }; return this; }
    public BaseScalarConstraintBuilder<TValue> Int32(int? minimum, int? maximum) { _value = _value with { MinimumInt32 = minimum, MaximumInt32 = maximum }; return this; }
    public BaseScalarConstraintBuilder<TValue> Int64(long? minimum, long? maximum) { _value = _value with { MinimumInt64 = minimum, MaximumInt64 = maximum }; return this; }
    public BaseScalarConstraintBuilder<TValue> UInt32(uint? minimum, uint? maximum) { _value = _value with { MinimumUInt32 = minimum, MaximumUInt32 = maximum }; return this; }
    public BaseScalarConstraintBuilder<TValue> UInt64(ulong? minimum, ulong? maximum) { _value = _value with { MinimumUInt64 = minimum, MaximumUInt64 = maximum }; return this; }
    public BaseScalarConstraintBuilder<TValue> Decimal(BaseDecimalValue? minimum, BaseDecimalValue? maximum) { _value = _value with { MinimumDecimal = minimum, MaximumDecimal = maximum }; return this; }
    public BaseScalarConstraintBuilder<TValue> EnumLiterals(params ReadOnlySpan<string> values) { _value = _value with { AllowedEnumLiterals = [.. values.ToArray().Order(StringComparer.Ordinal)] }; return this; }
    public BaseScalarConstraintBuilder<TValue> BinaryBytes(int maximum) { _value = _value with { MaximumBinaryBytes = maximum }; return this; }
    public BaseScalarConstraintBuilder<TValue> CanonicalJson(int maximumBytes, BaseJsonShape? shape = null, int? maximumDepth = null, int? maximumArrayItems = null, int? maximumObjectProperties = null, int? maximumTotalNodes = null, int? maximumTotalStringUtf8Bytes = null, int? maximumTotalNameUtf8Bytes = null)
    {
        _value = _value with { MaximumCanonicalJsonBytes = maximumBytes, JsonShape = shape, MaximumJsonDepth = maximumDepth, MaximumJsonArrayItems = maximumArrayItems, MaximumJsonObjectProperties = maximumObjectProperties, MaximumJsonTotalNodes = maximumTotalNodes, MaximumJsonTotalStringUtf8Bytes = maximumTotalStringUtf8Bytes, MaximumJsonTotalNameUtf8Bytes = maximumTotalNameUtf8Bytes };
        return this;
    }
    public BaseScalarConstraintBuilder<TValue> CollectionItems(int? minimum, int? maximum) { _value = _value with { MinimumCollectionItems = minimum, MaximumCollectionItems = maximum }; return this; }
    internal BaseScalarConstraintSet Build() => _value with { AllowedEnumLiterals = [.. _value.AllowedEnumLiterals] };
}

/// <summary>Builds one exact graph-owned logical index.</summary>
public sealed class BaseLogicalIndexBuilder<TRecord>
{
    private readonly string _collectionId;
    private readonly BaseLogicalIndexId _id;
    private readonly long _version;
    private readonly Func<string, int> _resolveOrdinal;
    private readonly List<BaseLogicalIndexPart> _parts = [];
    private bool _unique;
    private bool _storeRequired;
    private BaseIndexPredicateRegistry? _predicate;

    internal BaseLogicalIndexBuilder(string collectionId, BaseLogicalIndexId id, long version, Func<string, int> resolveOrdinal)
    { _collectionId = collectionId; _id = id; _version = version; _resolveOrdinal = resolveOrdinal; }
    internal BaseLogicalIndexId Id => _id;

    public BaseLogicalIndexBuilder<TRecord> Part<TValue>(BaseJsonProperty<TRecord, TValue> field, BaseIndexSortDirection direction = BaseIndexSortDirection.Ascending, BaseIndexCollation collation = BaseIndexCollation.OrdinalBinary, BaseIndexNullOrder nullOrder = BaseIndexNullOrder.MissingThenNullThenValue)
    {
        ArgumentNullException.ThrowIfNull(field);
        _parts.Add(new BaseLogicalIndexPart { FieldOrdinal = _resolveOrdinal(field.WireName), Direction = direction, Collation = collation, NullOrder = nullOrder });
        return this;
    }

    public BaseLogicalIndexBuilder<TRecord> Unique() { _unique = true; _storeRequired = true; return this; }
    public BaseLogicalIndexBuilder<TRecord> StoreRequired() { _storeRequired = true; return this; }
    public BaseLogicalIndexBuilder<TRecord> Predicate(Action<BaseIndexPredicateBuilder<TRecord>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_predicate is not null) throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        var builder = new BaseIndexPredicateBuilder<TRecord>(_resolveOrdinal);
        configure(builder);
        _predicate = builder.Build();
        return this;
    }

    internal BaseLogicalIndexDefinition Build(IReadOnlyList<FieldDefinition> fields)
    {
        BaseIndexPredicateRegistry predicate = _predicate ?? BaseSchemaContract.SealPredicate(BaseIndexPredicateId.Create("root"), [new BaseIndexPredicateNode { Id = BaseIndexPredicateId.Create("root"), Kind = BaseIndexPredicateNodeKind.True }]);
        return BaseSchemaContract.SealIndex(new BaseLogicalIndexDefinition { Id = _id, Version = _version, CollectionId = _collectionId, Parts = [.. _parts], Unique = _unique, StoreRequired = _storeRequired, MembershipPredicate = predicate, Checksum = default }, fields);
    }
}

/// <summary>Builds one closed, stable-identity partial-index predicate tree.</summary>
public sealed class BaseIndexPredicateBuilder<TRecord>
{
    private readonly Func<string, int> _resolveOrdinal;
    private readonly Dictionary<BaseIndexPredicateId, BaseIndexPredicateNode> _nodes = [];
    private BaseIndexPredicateId _root;
    internal BaseIndexPredicateBuilder(Func<string, int> resolveOrdinal) => _resolveOrdinal = resolveOrdinal;

    public BaseIndexPredicateId True(string id) => Add(id, BaseIndexPredicateNodeKind.True);
    public BaseIndexPredicateId False(string id) => Add(id, BaseIndexPredicateNodeKind.False);
    public BaseIndexPredicateId IsDefined<TValue>(string id, BaseJsonProperty<TRecord, TValue> field) => Field(id, BaseIndexPredicateNodeKind.IsDefined, field);
    public BaseIndexPredicateId IsMissing<TValue>(string id, BaseJsonProperty<TRecord, TValue> field) => Field(id, BaseIndexPredicateNodeKind.IsMissing, field);
    public BaseIndexPredicateId IsNull<TValue>(string id, BaseJsonProperty<TRecord, TValue> field) => Field(id, BaseIndexPredicateNodeKind.IsNull, field);
    public BaseIndexPredicateId IsNotNull<TValue>(string id, BaseJsonProperty<TRecord, TValue> field) => Field(id, BaseIndexPredicateNodeKind.IsNotNull, field);
    public BaseIndexPredicateId Equal<TValue>(string id, BaseJsonProperty<TRecord, TValue> field, TValue literal)
    {
        ArgumentNullException.ThrowIfNull(field); ArgumentNullException.ThrowIfNull(literal);
        BaseScalarKind kind = Kind(typeof(TValue)); Type valueType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        BaseScalarCodecAuthority codec = kind == BaseScalarKind.ClosedEnum ? BaseSchemaContract.Codec(kind, BaseSchemaContract.EnumQualifier(Enum.GetNames(valueType))) : BaseSchemaContract.Codec(kind);
        return Add(id, BaseIndexPredicateNodeKind.Equal, _resolveOrdinal(field.WireName), new BaseCanonicalScalarLiteral { Kind = kind, Codec = codec, CanonicalBytes = [.. BaseSchemaContract.EncodeLiteral(kind, literal!)] });
    }
    public BaseIndexPredicateId And(string id, params ReadOnlySpan<BaseIndexPredicateId> children) => Boolean(id, BaseIndexPredicateNodeKind.And, children);
    public BaseIndexPredicateId Or(string id, params ReadOnlySpan<BaseIndexPredicateId> children) => Boolean(id, BaseIndexPredicateNodeKind.Or, children);
    public BaseIndexPredicateId Not(string id, BaseIndexPredicateId child) => Add(id, BaseIndexPredicateNodeKind.Not, children: [child]);
    public BaseIndexPredicateBuilder<TRecord> Root(BaseIndexPredicateId root) { _root = root; return this; }

    internal BaseIndexPredicateRegistry Build()
    {
        if (!_root.IsValid) throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        return BaseSchemaContract.SealPredicate(_root, _nodes.Values);
    }

    private BaseIndexPredicateId Field<TValue>(string id, BaseIndexPredicateNodeKind kind, BaseJsonProperty<TRecord, TValue> field) { ArgumentNullException.ThrowIfNull(field); return Add(id, kind, _resolveOrdinal(field.WireName)); }
    private BaseIndexPredicateId Boolean(string id, BaseIndexPredicateNodeKind kind, ReadOnlySpan<BaseIndexPredicateId> children) => Add(id, kind, children: [.. children.ToArray().Order()]);
    private BaseIndexPredicateId Add(string text, BaseIndexPredicateNodeKind kind, int? fieldOrdinal = null, BaseCanonicalScalarLiteral? literal = null, ImmutableArray<BaseIndexPredicateId> children = default)
    {
        BaseIndexPredicateId id = BaseIndexPredicateId.Create(text);
        if (!_nodes.TryAdd(id, new BaseIndexPredicateNode { Id = id, Kind = kind, FieldOrdinal = fieldOrdinal, Literal = literal, Children = children.IsDefault ? [] : children })) throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        return id;
    }
    private static BaseScalarKind Kind(Type type)
    {
        Type actual = Nullable.GetUnderlyingType(type) ?? type;
        if (actual == typeof(string)) return BaseScalarKind.String; if (actual == typeof(BaseBinary)) return BaseScalarKind.Binary; if (actual == typeof(int)) return BaseScalarKind.Int32; if (actual == typeof(long)) return BaseScalarKind.Int64;
        if (actual == typeof(uint)) return BaseScalarKind.UInt32; if (actual == typeof(ulong)) return BaseScalarKind.UInt64; if (actual == typeof(decimal)) return BaseScalarKind.Decimal;
        if (actual == typeof(bool)) return BaseScalarKind.Boolean; if (actual == typeof(Guid)) return BaseScalarKind.Guid; if (actual == typeof(DateTimeOffset)) return BaseScalarKind.UtcDateTime; if (actual.IsEnum) return BaseScalarKind.ClosedEnum;
        if (actual == typeof(BaseCanonicalJson)) return BaseScalarKind.CanonicalJson;
        if (actual == typeof(BaseModuleGeneration)) return BaseScalarKind.ModuleGeneration;
        if (actual.IsArray) return BaseScalarKind.FrozenArray;
        throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
    }
}
