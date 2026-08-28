using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HPD.Base;

internal enum BaseModuleProgramValueProvenance
{
    Request = 0,
    HostConstant = 1,
    Provider = 2,
    StoredAuthority = 3,
}

internal readonly record struct BaseModuleProgramValue(
    bool Present,
    JsonElement Value,
    BaseModuleProgramValueProvenance Provenance)
{
    internal static BaseModuleProgramValue Missing(BaseModuleProgramValueProvenance provenance) =>
        new(false, default, provenance);
    internal bool IsNull => Present && Value.ValueKind == JsonValueKind.Null;
}

internal sealed class BaseModuleRequestLimitException : Exception { }

internal sealed class BaseModuleProgramEvaluator<TRequest, TResult>
{
    private readonly BaseRegisteredModuleMutationDefinition _definition;
    private readonly BaseGeneratedModuleMutationIdentity<TRequest, TResult> _identity;
    private readonly JsonElement _request;
    private readonly IReadOnlyDictionary<string, BaseCapturedModuleRecord> _records;
    private readonly IReadOnlyDictionary<string, BaseCapturedModuleGeneration> _generations;
    private readonly IReadOnlyDictionary<string, BaseModuleGuard> _guards;
    private readonly HashSet<string> _requestOnlyGuards;
    private readonly IReadOnlyDictionary<string, CollectionDefinition> _collections;
    private readonly Dictionary<string, bool> _guardValues;
    private readonly HashSet<string> _evaluatingGuards = new(StringComparer.Ordinal);
    private readonly ImmutableArray<BaseModuleDecisionTraceEntry>.Builder _decisions = ImmutableArray.CreateBuilder<BaseModuleDecisionTraceEntry>();
    private int _decisionOrdinal;
    private readonly BaseSemanticActivationCapturedState? _semanticState;
    private readonly BaseModuleMutationLimits? _requestLimits;
    private int _requestGuardEvaluations;
    private long _staticSetComparisons;

    internal BaseModuleProgramEvaluator(
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> identity,
        TRequest request,
        BaseCapturedAtomicExecution? captured,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        BaseModuleMutationLimits? requestLimits = null,
        IReadOnlyDictionary<string, bool>? establishedRequestGuards = null)
        : this(definition, identity,
            JsonSerializer.SerializeToElement(request, identity.RequestTypeInfo), captured,
            collections, requestLimits, establishedRequestGuards)
    {
    }

    internal BaseModuleProgramEvaluator(
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> identity,
        JsonElement request,
        BaseCapturedAtomicExecution? captured,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        BaseModuleMutationLimits? requestLimits = null,
        IReadOnlyDictionary<string, bool>? establishedRequestGuards = null)
    {
        _definition = definition;
        _identity = identity;
        _request = request.Clone();
        _records = captured?.ModuleRecords.ToDictionary(static value => value.CaptureId, StringComparer.Ordinal)
            ?? new Dictionary<string, BaseCapturedModuleRecord>(StringComparer.Ordinal);
        _generations = captured?.Generations.ToDictionary(static value => value.CaptureId, StringComparer.Ordinal)
            ?? new Dictionary<string, BaseCapturedModuleGeneration>(StringComparer.Ordinal);
        _guards = definition.Template.Guards.ToDictionary(static value => value.Id, StringComparer.Ordinal);
        _requestOnlyGuards = _guards.Keys.Where(id =>
            BaseModuleMutationContractValidator.IsRequestOnlyGuard(id, _guards))
            .ToHashSet(StringComparer.Ordinal);
        _guardValues = establishedRequestGuards is null
            ? new Dictionary<string, bool>(StringComparer.Ordinal)
            : establishedRequestGuards.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        _collections = collections;
        _semanticState = captured?.SemanticActivation?.State;
        _requestLimits = requestLimits;
    }

    internal ImmutableArray<BaseModuleDecisionTraceEntry> Decisions => _decisions.ToImmutable();
    internal IReadOnlyDictionary<string, bool> EstablishedRequestGuards => _guardValues
        .Where(pair => _requestOnlyGuards.Contains(pair.Key))
        .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
    internal void RecordIfDecision(string statementId, bool selected) =>
        RecordDecision(BaseModuleDecisionKind.IfStatement, statementId, selected);

    internal BaseModuleProgramValue Evaluate(BaseModuleValueExpression expression)
    {
        BaseModuleProgramValue value = expression switch
        {
        BaseModuleRequestPropertyExpression request => RequestProperty(request.Property),
        BaseModuleConstantExpression constant => Parse(constant.CanonicalBaseJson.AsSpan(), BaseModuleProgramValueProvenance.HostConstant),
        BaseModuleCapturedRecordIdExpression record => Record(record.CaptureId, static value => JsonValue(value.Current?.Id.Value, BaseModuleProgramValueProvenance.Provider)),
        BaseModuleCapturedRevisionExpression revision => Record(revision.CaptureId, static value => JsonValue(value.Current?.Metadata.Revision?.Value, BaseModuleProgramValueProvenance.Provider)),
        BaseModuleCapturedFieldExpression field => CapturedField(field.Field),
        BaseModuleCapturedGenerationExpression generation => Generation(generation.CaptureId),
        BaseModuleCoalesceExpression coalesce => Coalesce(coalesce),
        BaseModuleConditionalExpression conditional => Conditional(conditional),
        BaseModuleBinaryNumericExpression numeric => Numeric(numeric),
        BaseModuleRecordIdConversionExpression conversion => RecordIdConversion(conversion),
        BaseModuleGenerationKeyFromGuidExpression generationKey => GenerationKeyFromGuid(generationKey),
        BaseModuleMissingExpression => BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.HostConstant),
        BaseModulePresenceLiftExpression lift => Evaluate(lift.Source),
        BaseModuleIncarnationBytesExpression incarnation => IncarnationBytes(incarnation),
        BaseModuleSha256HexStringIdentityExpression identity => Sha256HexStringIdentity(identity),
        BaseModuleCommittedRecordIdExpression or BaseModuleCommittedRevisionExpression
            or BaseModuleCommittedUpsertDispositionExpression or BaseModuleResultingGenerationExpression =>
            throw new InvalidOperationException("base.moduleMutation.resultAuthorityRequired"),
            _ => throw new InvalidOperationException("base.moduleMutation.invalid"),
        };
        return Validate(expression.ResultType!, value);
    }

    internal bool Guard(string id)
    {
        if (_guardValues.TryGetValue(id, out bool cached)) return cached;
        if (!_guards.TryGetValue(id, out BaseModuleGuard? guard) || !_evaluatingGuards.Add(id))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        if (_requestOnlyGuards.Contains(id))
        {
            _requestGuardEvaluations = checked(_requestGuardEvaluations + 1);
            if (_requestLimits is { } limits && _requestGuardEvaluations > limits.MaximumRequestGuardEvaluations)
                throw new BaseModuleRequestLimitException();
        }
        bool value = guard switch
        {
            BaseModuleRecordPresenceGuard presence => _records.TryGetValue(presence.CaptureId, out BaseCapturedModuleRecord? record)
                && (record.Current is not null) == presence.MustBePresent,
            BaseModuleRevisionEqualsGuard revision => Equal(
                Record(revision.CaptureId, static value => JsonValue(value.Current?.Metadata.Revision?.Value, BaseModuleProgramValueProvenance.Provider)),
                Evaluate(revision.Expected)),
            BaseModuleFieldEqualsGuard field => Equal(CapturedField(field.Field), Evaluate(field.Expected)),
            BaseModuleFieldComparisonGuard field => OrderedCompare(
                CapturedField(field.Field), Evaluate(field.Expected), field.Field.Authority.Kind, field.Comparison),
            BaseModuleFieldPresenceGuard field => Presence(CapturedField(field.Field), field.Test),
            BaseModuleGenerationGuard generation => CompareGeneration(generation),
            BaseModuleSemanticActivationStateGuard semantic => _semanticState == semantic.Test switch
            {
                BaseModuleSemanticActivationStateTest.Missing => BaseSemanticActivationCapturedState.Missing,
                BaseModuleSemanticActivationStateTest.Live => BaseSemanticActivationCapturedState.Live,
                BaseModuleSemanticActivationStateTest.Retired => BaseSemanticActivationCapturedState.Retired,
                BaseModuleSemanticActivationStateTest.CompactedAbsent => BaseSemanticActivationCapturedState.CompactedAbsent,
                _ => throw new InvalidOperationException("base.moduleMutation.invalid"),
            },
            BaseModuleLogicalGuard logical => Logical(logical),
            BaseModuleValueEqualsGuard equality => Equal(Evaluate(equality.Left), Evaluate(equality.Right)),
            BaseModuleValueComparisonGuard comparison => OrderedCompare(
                Evaluate(comparison.Left), Evaluate(comparison.Right), comparison.Left.ResultType!.Kind, comparison.Comparison),
            BaseModuleValuePresenceGuard presence => Presence(Evaluate(presence.Value), presence.Test),
            BaseModuleSetGuard set => StaticSet(set),
            _ => throw new InvalidOperationException("base.moduleMutation.invalid"),
        };
        _evaluatingGuards.Remove(id);
        _guardValues.Add(id, value);
        return value;
    }

    private static bool OrderedCompare(
        BaseModuleProgramValue left,
        BaseModuleProgramValue right,
        BaseModuleValueKind kind,
        BaseModuleOrderedComparisonKind comparison)
    {
        if (!left.Present || !right.Present || left.IsNull || right.IsNull)
            return false;
        int order = kind switch
        {
            BaseModuleValueKind.Int32 => left.Value.GetInt32().CompareTo(right.Value.GetInt32()),
            BaseModuleValueKind.Int64 => left.Value.GetInt64().CompareTo(right.Value.GetInt64()),
            BaseModuleValueKind.UInt32 => left.Value.GetUInt32().CompareTo(right.Value.GetUInt32()),
            BaseModuleValueKind.UInt64 => left.Value.GetUInt64().CompareTo(right.Value.GetUInt64()),
            BaseModuleValueKind.Decimal => left.Value.GetDecimal().CompareTo(right.Value.GetDecimal()),
            BaseModuleValueKind.UtcDateTime => CompareDateTime(left.Value, right.Value),
            BaseModuleValueKind.Guid => string.CompareOrdinal(left.Value.GetString(), right.Value.GetString()),
            BaseModuleValueKind.String => string.CompareOrdinal(left.Value.GetString(), right.Value.GetString()),
            _ => throw new InvalidOperationException("base.moduleMutation.invalid"),
        };
        return comparison switch
        {
            BaseModuleOrderedComparisonKind.LessThan => order < 0,
            BaseModuleOrderedComparisonKind.LessThanOrEqual => order <= 0,
            BaseModuleOrderedComparisonKind.GreaterThan => order > 0,
            BaseModuleOrderedComparisonKind.GreaterThanOrEqual => order >= 0,
            _ => throw new InvalidOperationException("base.moduleMutation.invalid"),
        };

        static int CompareDateTime(JsonElement left, JsonElement right)
        {
            if (!BaseModuleDateTimeContract.TryRead(left, out DateTimeOffset leftValue)
                || !BaseModuleDateTimeContract.TryRead(right, out DateTimeOffset rightValue))
                throw new InvalidOperationException("base.moduleMutation.invalid");
            return leftValue.CompareTo(rightValue);
        }
    }

    internal BaseModuleProgramValue Object(BaseModuleObjectExpression expression, CollectionDefinition? collection)
    {
        var value = new JsonObject();
        BaseModuleProgramValueProvenance provenance = BaseModuleProgramValueProvenance.HostConstant;
        foreach (BaseModuleObjectPropertyExpression property in expression.Properties)
        {
            BaseModuleProgramValue evaluated = Evaluate(property.Value);
            provenance = Combine(provenance, evaluated.Provenance);
            if (!evaluated.Present) continue;
            string wireName = collection is null
                ? ResultWireName(property.StablePropertyId)
                : collection.Fields?.SingleOrDefault(field => string.Equals(field.Id, property.StablePropertyId, StringComparison.Ordinal))?.WireName
                    ?? throw new InvalidOperationException("base.moduleMutation.invalid");
            value.Add(wireName, JsonNode.Parse(evaluated.Value.GetRawText()));
        }
        return Parse(Encoding.UTF8.GetBytes(value.ToJsonString()), provenance);
    }

    internal TResult ProjectResult(
        BaseModuleResultProjection projection,
        IReadOnlyDictionary<string, BaseRecordMutationFact> committed,
        IReadOnlyDictionary<string, BaseModuleCommittedGeneration> generations,
        out ImmutableArray<byte> canonicalBytes) =>
        ProjectResult(projection, committed, generations, null, out canonicalBytes);

    internal TResult ProjectResult(
        BaseModuleResultProjection projection,
        IReadOnlyDictionary<string, BaseRecordMutationFact> committed,
        IReadOnlyDictionary<string, BaseModuleCommittedGeneration> generations,
        BaseSemanticActivationReceiptEvidence? semantic,
        out ImmutableArray<byte> canonicalBytes)
    {
        BaseModuleProgramValue EvaluateResult(BaseModuleValueExpression expression)
        {
            BaseModuleProgramValue value = expression switch
            {
            BaseModuleCommittedRecordIdExpression record => Committed(record.StatementId, static fact => JsonValue((fact.After ?? fact.Before)?.Id.Value, BaseModuleProgramValueProvenance.StoredAuthority), committed),
            BaseModuleCommittedRevisionExpression revision => Committed(revision.StatementId, static fact => JsonValue(fact.After?.Metadata.Revision?.Value, BaseModuleProgramValueProvenance.StoredAuthority), committed),
            BaseModuleCommittedUpsertDispositionExpression upsert => Committed(upsert.StatementId, static fact => JsonValue(fact.UpsertOutcome?.ToString(), BaseModuleProgramValueProvenance.StoredAuthority), committed),
            BaseModuleResultingGenerationExpression generation => generations.TryGetValue(generation.CaptureId, out BaseModuleCommittedGeneration? committedGeneration)
                ? JsonValue(committedGeneration.Resulting.ToCanonicalString(), BaseModuleProgramValueProvenance.StoredAuthority) : BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.StoredAuthority),
            BaseModuleSemanticActivationDispositionExpression => semantic?.EnsureDisposition is { } ensure
                ? JsonValue(EnsureWire(ensure), BaseModuleProgramValueProvenance.StoredAuthority) : BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.StoredAuthority),
            BaseModuleSemanticActivationIdExpression => semantic?.ActivationId is { } activationId
                ? JsonValue(activationId, BaseModuleProgramValueProvenance.StoredAuthority) : BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.StoredAuthority),
            BaseModuleSemanticActivationWasMaterializedExpression => semantic?.EnsureDisposition is { } materialized
                ? JsonValue(materialized == BaseSemanticActivationEnsureDisposition.Created, BaseModuleProgramValueProvenance.StoredAuthority) : BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.StoredAuthority),
            BaseModuleSemanticActivationRetirementDispositionExpression => semantic?.RetirementDisposition is { } retirement
                ? JsonValue(RetirementWire(retirement), BaseModuleProgramValueProvenance.StoredAuthority) : BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.StoredAuthority),
            BaseModuleCoalesceExpression coalesce => coalesce.Values.Select(EvaluateResult).FirstOrDefault(static value => value.Present),
            BaseModuleConditionalExpression conditional => ResultConditional(conditional, EvaluateResult),
                _ => Evaluate(expression),
            };
            return Validate(expression.ResultType!, value);
        }
        BaseModuleProgramValue result = ResultObject(projection.Value, EvaluateResult);
        if (!result.Present) throw new InvalidOperationException("base.moduleMutation.resultInvalid");
        byte[] projectedBytes = Encoding.UTF8.GetBytes(result.Value.GetRawText());
        ValidateDto(projectedBytes, _identity.ResultBindings, providerInfluenced: true);
        TResult? typed = JsonSerializer.Deserialize(projectedBytes, _identity.ResultTypeInfo);
        if (typed is null) throw new InvalidOperationException("base.moduleMutation.resultInvalid");
        canonicalBytes = projectedBytes.ToImmutableArray();
        return typed;
    }

    internal static void ValidateDto(
        ReadOnlySpan<byte> canonicalBytes,
        IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> bindings,
        bool providerInfluenced)
    {
        using JsonDocument document = JsonDocument.Parse(canonicalBytes.ToArray());
        ValidateDeclaredShape(document.RootElement, bindings.Values.Select(static binding => binding.WirePropertyPath).ToArray(), 0, providerInfluenced);
        foreach (BaseModuleDtoPropertyBinding binding in bindings.Values)
        {
            JsonElement current = document.RootElement;
            bool present = true;
            foreach (string wire in binding.WirePropertyPath)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(wire, out current))
                {
                    present = false;
                    break;
                }
            }
            Validate(binding.ScalarAuthority.ValueType,
                present ? new BaseModuleProgramValue(true, current, providerInfluenced
                    ? BaseModuleProgramValueProvenance.StoredAuthority : BaseModuleProgramValueProvenance.Request)
                    : BaseModuleProgramValue.Missing(providerInfluenced
                        ? BaseModuleProgramValueProvenance.StoredAuthority : BaseModuleProgramValueProvenance.Request));
        }
    }

    private static void ValidateDeclaredShape(
        JsonElement value,
        IReadOnlyList<IReadOnlyList<string>> paths,
        int depth,
        bool providerInfluenced)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new BaseModuleScalarContractException(providerInfluenced);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new BaseModuleScalarContractException(providerInfluenced);
            IReadOnlyList<IReadOnlyList<string>> matching = paths
                .Where(path => path.Count > depth && string.Equals(path[depth], property.Name, StringComparison.Ordinal))
                .ToArray();
            if (matching.Count == 0)
                throw new BaseModuleScalarContractException(providerInfluenced);
            if (matching.Any(path => path.Count == depth + 1))
                continue;
            ValidateDeclaredShape(property.Value, matching, depth + 1, providerInfluenced);
        }
    }

    private BaseModuleProgramValue ResultObject(
        BaseModuleObjectExpression expression,
        Func<BaseModuleValueExpression, BaseModuleProgramValue> evaluate)
    {
        var value = new JsonObject();
        BaseModuleProgramValueProvenance provenance = BaseModuleProgramValueProvenance.HostConstant;
        foreach (BaseModuleObjectPropertyExpression property in expression.Properties)
        {
            BaseModuleProgramValue evaluated = evaluate(property.Value);
            provenance = Combine(provenance, evaluated.Provenance);
            if (!evaluated.Present) continue;
            value.Add(ResultWireName(property.StablePropertyId), JsonNode.Parse(evaluated.Value.GetRawText()));
        }
        return Parse(Encoding.UTF8.GetBytes(value.ToJsonString()), provenance);
    }

    private BaseModuleProgramValue ResultConditional(
        BaseModuleConditionalExpression conditional,
        Func<BaseModuleValueExpression, BaseModuleProgramValue> evaluate)
    {
        bool selected = Guard(conditional.GuardId);
        RecordDecision(BaseModuleDecisionKind.ConditionalExpression, conditional.Id, selected);
        return evaluate(selected ? conditional.WhenTrue : conditional.WhenFalse);
    }

    private BaseModuleProgramValue RequestProperty(BaseModuleRequestPropertyReference reference)
    {
        JsonElement current = _request;
        var path = new List<string>();
        foreach (string stableId in reference.StablePropertyPath)
        {
            path.Add(stableId);
            if (!_identity.RequestBindings.TryGetValue(string.Join('\0', path), out BaseModuleDtoPropertyBinding? binding))
                throw new InvalidOperationException("base.moduleMutation.invalid");
            string wireName = binding.WirePropertyPath[^1];
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(wireName, out current))
                return BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.Request);
        }
        return new(true, current.Clone(), BaseModuleProgramValueProvenance.Request);
    }

    private static BaseModuleProgramValue Validate(BaseModuleValueType authority, BaseModuleProgramValue value)
    {
        bool valid = !value.Present
            ? authority.Presence == BaseFieldPresence.Optional
            : value.IsNull
                ? authority.Nullability == BaseFieldNullability.Nullable
                : ScalarValid(authority, value.Value);
        if (!valid) throw new BaseModuleScalarContractException(value.Provenance is
            BaseModuleProgramValueProvenance.Provider or BaseModuleProgramValueProvenance.StoredAuthority);
        return value;
    }

    private static bool ScalarValid(BaseModuleValueType authority, JsonElement value)
    {
        if (authority.Kind == BaseModuleValueKind.Revision)
        {
            if (value.ValueKind != JsonValueKind.String) return false;
            try { return new RevisionToken(value.GetString()!).IsValid; }
            catch (ArgumentException) { return false; }
        }
        if (authority.Kind == BaseModuleValueKind.SubjectReference)
        {
            if (authority.SubjectQualifier is not { } qualifier) return false;
            try
            {
                _ = BaseSubjectReferenceEncoding.DecodeElement(
                    value, qualifier.SubjectIdKind, qualifier.MaximumSubjectIdUtf8Bytes);
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
            {
                return false;
            }
        }
        if (authority.Kind == BaseModuleValueKind.SubjectIncarnation)
        {
            if (value.ValueKind != JsonValueKind.String) return false;
            try { _ = BaseSubjectIncarnation.Parse(value.GetString()!); return true; }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
            { return false; }
        }
        var field = new FieldDefinition
        {
            Id = "value", ApplicationName = "value", WireName = "value", Type = "scalar",
            Presence = authority.Presence, Nullability = authority.Nullability,
            ScalarKind = (BaseScalarKind)(int)authority.Kind, ScalarCodec = authority.Codec,
            ScalarConstraints = authority.Constraints, ScalarConstraintChecksum = authority.ConstraintChecksum,
        };

        return BaseCanonicalRecordValidator.Validate(field, value) is null;
    }

    private static string EnsureWire(BaseSemanticActivationEnsureDisposition value) => value switch
    {
        BaseSemanticActivationEnsureDisposition.Created => "created",
        BaseSemanticActivationEnsureDisposition.Existing => "existing",
        BaseSemanticActivationEnsureDisposition.Retired => "retired",
        _ => throw new InvalidOperationException("base.moduleMutation.providerResultInvalid"),
    };

    private static string RetirementWire(BaseSemanticActivationRetirementDisposition value) => value switch
    {
        BaseSemanticActivationRetirementDisposition.RetiredNow => "retiredNow",
        BaseSemanticActivationRetirementDisposition.AlreadyRetired => "alreadyRetired",
        BaseSemanticActivationRetirementDisposition.AlreadyCompacted => "alreadyCompacted",
        _ => throw new InvalidOperationException("base.moduleMutation.providerResultInvalid"),
    };

    private string ResultWireName(string stableId)
    {
        if (!_identity.ResultBindings.TryGetValue(stableId, out BaseModuleDtoPropertyBinding? binding))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        if (binding.WirePropertyPath.Count != 1)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        return binding.WirePropertyPath[0];
    }

    private BaseModuleProgramValue CapturedField(BaseModuleCapturedFieldReference reference)
    {
        if (!_records.TryGetValue(reference.CaptureId, out BaseCapturedModuleRecord? captured) || captured.Current is null)
            return BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.Provider);
        CollectionDefinition collection = _collections[captured.CollectionId];
        string wire = collection.Fields?.SingleOrDefault(field => string.Equals(field.Id, reference.StableFieldId, StringComparison.Ordinal))?.WireName
            ?? throw new InvalidOperationException("base.moduleMutation.invalid");
        return captured.Current.Payload.Fields?.TryGetValue(wire, out JsonElement value) == true
            ? new(true, value.Clone(), BaseModuleProgramValueProvenance.Provider)
            : BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.Provider);
    }

    private BaseModuleProgramValue Generation(string captureId) =>
        _generations.TryGetValue(captureId, out BaseCapturedModuleGeneration? value) && value.Generation is not null
            ? JsonValue(value.Generation.ToCanonicalString(), BaseModuleProgramValueProvenance.Provider)
            : BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.Provider);

    private BaseModuleProgramValue IncarnationBytes(BaseModuleIncarnationBytesExpression expression)
    {
        BaseModuleProgramValue source = Evaluate(expression.Source);
        if (!source.Present || source.IsNull || source.Value.ValueKind != JsonValueKind.String)
            throw new BaseModuleScalarContractException(source.Provenance is
                BaseModuleProgramValueProvenance.Provider or BaseModuleProgramValueProvenance.StoredAuthority);
        BaseSubjectIncarnation incarnation = BaseSubjectIncarnation.Parse(source.Value.GetString()!);
        return JsonValue(Convert.ToBase64String(incarnation.ToArray()), source.Provenance);
    }

    private BaseModuleProgramValue Sha256HexStringIdentity(BaseModuleSha256HexStringIdentityExpression expression)
    {
        BaseModuleProgramValue source = Evaluate(expression.Source);
        if (!source.Present || source.IsNull || source.Value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        byte[] domain = Encoding.ASCII.GetBytes(expression.Domain);
        byte[] component = Encoding.UTF8.GetBytes(source.Value.GetString()!);
        byte[] preimage = new byte[domain.Length + 1 + sizeof(uint) + component.Length];
        domain.CopyTo(preimage, 0);
        BinaryPrimitives.WriteUInt32BigEndian(preimage.AsSpan(domain.Length + 1, sizeof(uint)), checked((uint)component.Length));
        component.CopyTo(preimage, domain.Length + 1 + sizeof(uint));
        return JsonValue(Convert.ToHexStringLower(SHA256.HashData(preimage)), source.Provenance);
    }

    private bool CompareGeneration(BaseModuleGenerationGuard guard)
    {
        BaseModuleProgramValue actual = Generation(guard.CaptureId);
        return guard.Comparison switch
        {
            BaseModuleGenerationComparisonKind.MustExist => actual.Present,
            BaseModuleGenerationComparisonKind.MustBeMissing => !actual.Present,
            BaseModuleGenerationComparisonKind.MustEqual => guard.Expected is not null && Equal(actual, Evaluate(guard.Expected)),
            _ => false,
        };
    }

    private bool Logical(BaseModuleLogicalGuard guard) => guard.Kind switch
    {
        BaseModuleLogicalGuardKind.And => guard.ChildGuardIds.All(Guard),
        BaseModuleLogicalGuardKind.Or => guard.ChildGuardIds.Any(Guard),
        BaseModuleLogicalGuardKind.Not when guard.ChildGuardIds.Length == 1 => !Guard(guard.ChildGuardIds[0]),
        _ => throw new InvalidOperationException("base.moduleMutation.invalid"),
    };

    private BaseModuleProgramValue Coalesce(BaseModuleCoalesceExpression expression)
    {
        foreach (BaseModuleValueExpression candidate in expression.Values)
        {
            BaseModuleProgramValue value = Evaluate(candidate);
            if (value.Present) return value;
        }
        return BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.Request);
    }

    private BaseModuleProgramValue Conditional(BaseModuleConditionalExpression expression)
    {
        bool selected = Guard(expression.GuardId);
        RecordDecision(BaseModuleDecisionKind.ConditionalExpression, expression.Id, selected);
        return Evaluate(selected ? expression.WhenTrue : expression.WhenFalse);
    }

    private BaseModuleProgramValue Numeric(BaseModuleBinaryNumericExpression expression)
    {
        BaseModuleProgramValue left = Evaluate(expression.Left);
        BaseModuleProgramValue right = Evaluate(expression.Right);
        BaseModuleProgramValueProvenance provenance = Combine(left.Provenance, right.Provenance);
        if (!left.Present || !right.Present) return BaseModuleProgramValue.Missing(provenance);
        if (expression.Operator is BaseModuleNumericOperator.IntegerAddChecked or BaseModuleNumericOperator.IntegerSubtractChecked)
        {
            long l = left.Value.GetInt64();
            long r = right.Value.GetInt64();
            return JsonValue(expression.Operator == BaseModuleNumericOperator.IntegerAddChecked ? checked(l + r) : checked(l - r), provenance);
        }
        decimal ld = left.Value.GetDecimal();
        decimal rd = right.Value.GetDecimal();
        decimal result = expression.Operator switch
        {
            BaseModuleNumericOperator.DecimalAddChecked => checked(ld + rd),
            BaseModuleNumericOperator.DecimalSubtractChecked => checked(ld - rd),
            BaseModuleNumericOperator.DecimalMultiplyChecked => checked(ld * rd),
            BaseModuleNumericOperator.Minimum => Math.Min(ld, rd),
            BaseModuleNumericOperator.Maximum => Math.Max(ld, rd),
            _ => throw new InvalidOperationException("base.moduleMutation.invalid"),
        };
        if (expression.Decimal is { } context)
            result = decimal.Round(result, context.Scale, context.Rounding switch
            {
                BaseModuleDecimalRounding.ToEven => MidpointRounding.ToEven,
                BaseModuleDecimalRounding.AwayFromZero => MidpointRounding.AwayFromZero,
                BaseModuleDecimalRounding.TowardZero => MidpointRounding.ToZero,
                _ => throw new InvalidOperationException("base.moduleMutation.invalid"),
            });
        return JsonValue(result, provenance);
    }

    private BaseModuleProgramValue RecordIdConversion(BaseModuleRecordIdConversionExpression expression)
    {
        BaseModuleProgramValue source = Evaluate(expression.Source);
        bool valid = source.Present && !source.IsNull && source.Value.ValueKind == JsonValueKind.String;
        string? text = valid ? source.Value.GetString() : null;
        valid = valid && text is not null && expression.Conversion switch
        {
            BaseModuleRecordIdConversionKind.CanonicalGuidD =>
                Guid.TryParseExact(text, "D", out Guid parsed)
                && string.Equals(parsed.ToString("D", CultureInfo.InvariantCulture), text, StringComparison.Ordinal),
            BaseModuleRecordIdConversionKind.CanonicalString => RecordId.TryParse(text, out _),
            _ => false,
        };
        if (!valid)
            throw new BaseModuleScalarContractException(source.Provenance is
                BaseModuleProgramValueProvenance.Provider or BaseModuleProgramValueProvenance.StoredAuthority);
        return JsonValue(text, source.Provenance);
    }

    private BaseModuleProgramValue GenerationKeyFromGuid(BaseModuleGenerationKeyFromGuidExpression expression)
    {
        BaseModuleProgramValue source = Evaluate(expression.Source);
        bool valid = source.Present && !source.IsNull && source.Value.ValueKind == JsonValueKind.String;
        string? text = valid ? source.Value.GetString() : null;
        valid = valid && text is not null && Guid.TryParseExact(text, "D", out Guid parsed)
            && string.Equals(parsed.ToString("D", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        if (!valid)
            throw new BaseModuleScalarContractException(source.Provenance is
                BaseModuleProgramValueProvenance.Provider or BaseModuleProgramValueProvenance.StoredAuthority);
        return JsonValue(text, source.Provenance);
    }

    private bool StaticSet(BaseModuleSetGuard guard)
    {
        BaseModuleProgramValue[] left = Enabled(guard.Left);
        BaseModuleProgramValue[]? right = guard.Right is null ? null : Enabled(guard.Right);
        return guard.Predicate switch
        {
            BaseModuleStaticSetPredicateKind.AllDistinct => AllDistinct(left),
            BaseModuleStaticSetPredicateKind.StrictlyIncreasing => StrictlyIncreasing(left, guard.Left.ElementType.Kind),
            BaseModuleStaticSetPredicateKind.Disjoint when right is not null => Disjoint(left, right),
            _ => throw new InvalidOperationException("base.moduleMutation.invalid"),
        };

        BaseModuleProgramValue[] Enabled(BaseModuleStaticSet set)
        {
            var values = new List<BaseModuleProgramValue>(set.Members.Length);
            foreach (BaseModuleStaticSetMember member in set.Members)
                if (member.EnableGuardId is null || Guard(member.EnableGuardId))
                    values.Add(Evaluate(member.Value));
            return [.. values];
        }

        bool AllDistinct(BaseModuleProgramValue[] values)
        {
            bool distinct = true;
            for (int leftIndex = 0; leftIndex < values.Length; leftIndex++)
                for (int rightIndex = leftIndex + 1; rightIndex < values.Length; rightIndex++)
                {
                    CountComparison();
                    if (Equal(values[leftIndex], values[rightIndex])) distinct = false;
                }
            return distinct;
        }

        bool StrictlyIncreasing(BaseModuleProgramValue[] values, BaseModuleValueKind kind)
        {
            bool increasing = true;
            for (int index = 1; index < values.Length; index++)
            {
                CountComparison();
                if (!OrderedCompare(values[index - 1], values[index], kind,
                    BaseModuleOrderedComparisonKind.LessThan)) increasing = false;
            }
            return increasing;
        }

        bool Disjoint(BaseModuleProgramValue[] left, BaseModuleProgramValue[] right)
        {
            bool disjoint = true;
            foreach (BaseModuleProgramValue leftValue in left)
                foreach (BaseModuleProgramValue rightValue in right)
                {
                    CountComparison();
                    if (Equal(leftValue, rightValue)) disjoint = false;
                }
            return disjoint;
        }

        void CountComparison()
        {
            _staticSetComparisons = checked(_staticSetComparisons + 1);
            if (_requestLimits is { } limits && _staticSetComparisons > limits.MaximumStaticSetComparisons)
                throw new BaseModuleRequestLimitException();
        }
    }

    private void RecordDecision(BaseModuleDecisionKind kind, string id, bool selected) =>
        _decisions.Add(new BaseModuleDecisionTraceEntry
        {
            EvaluationOrdinal = _decisionOrdinal++, Kind = kind, DecisionId = id, SelectedTrue = selected,
        });

    private static BaseModuleProgramValue Record(
        string captureId,
        Func<BaseCapturedModuleRecord, BaseModuleProgramValue> project,
        IReadOnlyDictionary<string, BaseCapturedModuleRecord>? records = null) =>
        records is not null && records.TryGetValue(captureId, out BaseCapturedModuleRecord? value)
            ? project(value) : BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.Provider);

    private BaseModuleProgramValue Record(string captureId, Func<BaseCapturedModuleRecord, BaseModuleProgramValue> project) =>
        _records.TryGetValue(captureId, out BaseCapturedModuleRecord? value) ? project(value) : BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.Provider);

    private static BaseModuleProgramValue Committed(
        string statementId,
        Func<BaseRecordMutationFact, BaseModuleProgramValue> project,
        IReadOnlyDictionary<string, BaseRecordMutationFact> committed) =>
        committed.TryGetValue(statementId, out BaseRecordMutationFact? fact) ? project(fact) : BaseModuleProgramValue.Missing(BaseModuleProgramValueProvenance.StoredAuthority);

    private static bool Presence(BaseModuleProgramValue value, BaseModuleFieldPresenceTest test) => test switch
    {
        BaseModuleFieldPresenceTest.Missing => !value.Present,
        BaseModuleFieldPresenceTest.Null => value.IsNull,
        BaseModuleFieldPresenceTest.PresentValue => value.Present && !value.IsNull,
        _ => false,
    };

    private static bool Equal(BaseModuleProgramValue left, BaseModuleProgramValue right) =>
        left.Present == right.Present && (!left.Present || left.Value.ValueKind == right.Value.ValueKind
            && string.Equals(left.Value.GetRawText(), right.Value.GetRawText(), StringComparison.Ordinal));

    private static BaseModuleProgramValue Parse(ReadOnlySpan<byte> bytes, BaseModuleProgramValueProvenance provenance)
    {
        using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
        return new(true, document.RootElement.Clone(), provenance);
    }

    private static BaseModuleProgramValue JsonValue<T>(T value, BaseModuleProgramValueProvenance provenance)
    {
        if (value is null) return BaseModuleProgramValue.Missing(provenance);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            switch (value)
            {
                case string text: writer.WriteStringValue(text); break;
                case long integer: writer.WriteNumberValue(integer); break;
                case int integer: writer.WriteNumberValue(integer); break;
                case decimal number: writer.WriteNumberValue(number); break;
                case bool boolean: writer.WriteBooleanValue(boolean); break;
                default: throw new InvalidOperationException("base.moduleMutation.invalid");
            }
        }
        return Parse(buffer.WrittenSpan, provenance);
    }

    private static BaseModuleProgramValueProvenance Combine(
        BaseModuleProgramValueProvenance left,
        BaseModuleProgramValueProvenance right) => (BaseModuleProgramValueProvenance)Math.Max((int)left, (int)right);
}

internal sealed class BaseModuleScalarContractException(bool providerInfluenced) : Exception
{
    internal bool ProviderInfluenced { get; } = providerInfluenced;
}
