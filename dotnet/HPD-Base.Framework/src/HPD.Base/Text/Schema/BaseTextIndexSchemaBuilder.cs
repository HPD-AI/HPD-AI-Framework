using System.Collections.Immutable;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

/// <summary>Builds one manual provider-neutral lexical-index declaration from frozen serializer handles.</summary>
public sealed class BaseTextIndexSchemaBuilder<T>
{
    private readonly string _collectionId; private readonly int _version; private readonly JsonTypeInfo<T> _owner; private readonly Func<string, FieldDefinition> _resolve;
    private readonly List<(FieldDefinition Field, int Weight)> _fields = []; private readonly List<FieldDefinition> _filters = []; private string? _analyzer; private HPDBaseEndpointAudience? _audience; private BaseTextExecutionLimits? _limits; private bool _sealed;
    internal BaseTextIndexSchemaBuilder(string collectionId, string id, int version, JsonTypeInfo<T> owner, Func<string, FieldDefinition> resolve) { _collectionId = collectionId; Id = id; _version = version; _owner = owner; _resolve = resolve; }
    internal string Id { get; }
    /// <summary>Selects the exact portable analyzer contract.</summary>
    public BaseTextIndexSchemaBuilder<T> Analyzer(string analyzerContractId) { Mutable(); if (_analyzer is not null || analyzerContractId != BaseTextAnalyzers.UnicodeCaseFoldedV1) throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid); _analyzer = analyzerContractId; return this; }
    /// <summary>Adds one searchable string field and its integer weight.</summary>
    public BaseTextIndexSchemaBuilder<T> Field(BaseJsonProperty<T, string> property, int weight) { Mutable(); ArgumentNullException.ThrowIfNull(property); if (!ReferenceEquals(property.Owner, _owner) || weight is < 1 or > 16 || _fields.Count >= BaseTextPlatform.ProviderCapability(BaseTextProviderClass.CoLocatedTransactional).MaximumFieldsPerIndex) throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid); FieldDefinition field = _resolve(property.WireName); if (field.Type != "string" || field.Confidentiality is BaseFieldConfidentiality.Confidential or BaseFieldConfidentiality.Secret || _fields.Any(value => value.Field.Id == field.Id)) throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid); _fields.Add((field, weight)); return this; }
    /// <summary>Adds one ordinary pre-ranking filter field.</summary>
    public BaseTextIndexSchemaBuilder<T> FilterField<TValue>(BaseJsonProperty<T, TValue> property) { Mutable(); ArgumentNullException.ThrowIfNull(property); if (!ReferenceEquals(property.Owner, _owner) || _filters.Count >= BaseTextPlatform.ProviderCapability(BaseTextProviderClass.CoLocatedTransactional).MaximumFilterFields) throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid); FieldDefinition field = _resolve(property.WireName); _ = FilterKind(field); if (_filters.Any(value => value.Id == field.Id)) throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid); _filters.Add(field); return this; }
    /// <summary>Selects the exact generated endpoint audience.</summary>
    public BaseTextIndexSchemaBuilder<T> Audience(HPDBaseEndpointAudience audience) { Mutable(); if (_audience is not null || !Enum.IsDefined(audience)) throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid); _audience = audience; return this; }
    /// <summary>Selects the complete bounded execution profile.</summary>
    public BaseTextIndexSchemaBuilder<T> Limits(BaseTextExecutionLimits limits) { Mutable(); ArgumentNullException.ThrowIfNull(limits); if (_limits is not null) throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid); _limits = limits with { }; return this; }
    internal void Seal() { Mutable(); if (_fields.Count == 0) throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid); _sealed = true; }
    internal BaseTextIndexDefinition Build()
    {
        if (!_sealed) throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid); HPDBaseEndpointAudience audience = _audience ?? HPDBaseEndpointAudience.Application;
        return new BaseTextIndexDefinition
        {
            Id = Id, Version = _version, CollectionId = _collectionId, Audience = audience,
            Fields = _fields.Select(value => new BaseTextIndexFieldDefinition { StableFieldId = value.Field.Id, ApplicationName = value.Field.ApplicationName, WireName = value.Field.WireName, Weight = value.Weight, Confidentiality = value.Field.Confidentiality, StaticInfluenceAudiences = [audience], RequiresDynamicInfluenceConstraint = value.Field.Confidentiality == BaseFieldConfidentiality.Internal }).ToImmutableArray(),
            FilterFields = _filters.OrderBy(static value => value.Id, StringComparer.Ordinal).Select(value => new BaseTextIndexFilterFieldDefinition { StableFieldId = value.Id, ApplicationName = value.ApplicationName, WireName = value.WireName, ValueKind = FilterKind(value) }).ToImmutableArray(),
            AnalyzerContractId = _analyzer ?? BaseTextAnalyzers.UnicodeCaseFoldedV1, AnalyzerReceipt = BaseTextContractReceipts.AnalyzerReceipt, ScoringContractId = BaseTextScoring.ContractId, ScoringReceipt = BaseTextContractReceipts.ScoringReceipt, Limits = _limits ?? BaseTextPlatform.DefaultLimits, SerializerGraphChecksum = [], DefinitionChecksum = [],
        };
    }
    private void Mutable() { if (_sealed) throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid); }
    private static BaseTextFilterValueKind FilterKind(FieldDefinition field) => field.Type switch { "string" when field.Format == "record-id" => BaseTextFilterValueKind.Id, "string" => BaseTextFilterValueKind.String, "boolean" => BaseTextFilterValueKind.Boolean, "integer" => BaseTextFilterValueKind.Integer, _ => throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid) };
}
