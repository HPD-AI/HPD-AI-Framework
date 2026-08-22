using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text;
using System.Buffers;

namespace HPD.Base;

internal readonly record struct BaseModuleProgramValue(bool Present, JsonElement Value)
{
    internal static BaseModuleProgramValue Missing => new(false, default);
    internal bool IsNull => Present && Value.ValueKind == JsonValueKind.Null;
}

internal sealed class BaseModuleProgramEvaluator<TRequest, TResult>
{
    private readonly BaseRegisteredModuleMutationDefinition _definition;
    private readonly BaseGeneratedModuleMutationIdentity<TRequest, TResult> _identity;
    private readonly JsonElement _request;
    private readonly IReadOnlyDictionary<string, BaseCapturedModuleRecord> _records;
    private readonly IReadOnlyDictionary<string, BaseCapturedModuleGeneration> _generations;
    private readonly IReadOnlyDictionary<string, BaseModuleGuard> _guards;
    private readonly IReadOnlyDictionary<string, CollectionDefinition> _collections;
    private readonly Dictionary<string, bool> _guardValues = new(StringComparer.Ordinal);
    private readonly HashSet<string> _evaluatingGuards = new(StringComparer.Ordinal);
    private readonly ImmutableArray<BaseModuleDecisionTraceEntry>.Builder _decisions = ImmutableArray.CreateBuilder<BaseModuleDecisionTraceEntry>();
    private int _decisionOrdinal;

    internal BaseModuleProgramEvaluator(
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> identity,
        TRequest request,
        BaseCapturedAtomicExecution? captured,
        IReadOnlyDictionary<string, CollectionDefinition> collections)
    {
        _definition = definition;
        _identity = identity;
        _request = JsonSerializer.SerializeToElement(request, identity.RequestTypeInfo).Clone();
        _records = captured?.ModuleRecords.ToDictionary(static value => value.CaptureId, StringComparer.Ordinal)
            ?? new Dictionary<string, BaseCapturedModuleRecord>(StringComparer.Ordinal);
        _generations = captured?.Generations.ToDictionary(static value => value.CaptureId, StringComparer.Ordinal)
            ?? new Dictionary<string, BaseCapturedModuleGeneration>(StringComparer.Ordinal);
        _guards = definition.Template.Guards.ToDictionary(static value => value.Id, StringComparer.Ordinal);
        _collections = collections;
    }

    internal ImmutableArray<BaseModuleDecisionTraceEntry> Decisions => _decisions.ToImmutable();
    internal void RecordIfDecision(string statementId, bool selected) =>
        RecordDecision(BaseModuleDecisionKind.IfStatement, statementId, selected);

    internal BaseModuleProgramValue Evaluate(BaseModuleValueExpression expression) => expression switch
    {
        BaseModuleRequestPropertyExpression request => RequestProperty(request.Property),
        BaseModuleConstantExpression constant => Parse(constant.CanonicalBaseJson.AsSpan()),
        BaseModuleCapturedRecordIdExpression record => Record(record.CaptureId, static value => JsonValue(value.Current?.Id.Value)),
        BaseModuleCapturedRevisionExpression revision => Record(revision.CaptureId, static value => JsonValue(value.Current?.Metadata.Revision?.Value)),
        BaseModuleCapturedFieldExpression field => CapturedField(field.Field),
        BaseModuleCapturedGenerationExpression generation => Generation(generation.CaptureId),
        BaseModuleCoalesceExpression coalesce => Coalesce(coalesce),
        BaseModuleConditionalExpression conditional => Conditional(conditional),
        BaseModuleBinaryNumericExpression numeric => Numeric(numeric),
        BaseModuleObjectExpression value => Object(value, null),
        BaseModuleCommittedRecordIdExpression or BaseModuleCommittedRevisionExpression
            or BaseModuleCommittedUpsertDispositionExpression or BaseModuleResultingGenerationExpression =>
            throw new InvalidOperationException("base.moduleMutation.resultAuthorityRequired"),
        _ => throw new InvalidOperationException("base.moduleMutation.invalid"),
    };

    internal bool Guard(string id)
    {
        if (_guardValues.TryGetValue(id, out bool cached)) return cached;
        if (!_guards.TryGetValue(id, out BaseModuleGuard? guard) || !_evaluatingGuards.Add(id))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        bool value = guard switch
        {
            BaseModuleRecordPresenceGuard presence => _records.TryGetValue(presence.CaptureId, out BaseCapturedModuleRecord? record)
                && (record.Current is not null) == presence.MustBePresent,
            BaseModuleRevisionEqualsGuard revision => Equal(
                Record(revision.CaptureId, static value => JsonValue(value.Current?.Metadata.Revision?.Value)),
                Evaluate(revision.Expected)),
            BaseModuleFieldEqualsGuard field => Equal(CapturedField(field.Field), Evaluate(field.Expected)),
            BaseModuleFieldComparisonGuard field => OrderedCompare(
                CapturedField(field.Field), Evaluate(field.Expected), field.Field.DeclaredTypeId, field.Comparison),
            BaseModuleFieldPresenceGuard field => Presence(CapturedField(field.Field), field.Test),
            BaseModuleGenerationGuard generation => CompareGeneration(generation),
            BaseModuleLogicalGuard logical => Logical(logical),
            _ => throw new InvalidOperationException("base.moduleMutation.invalid"),
        };
        _evaluatingGuards.Remove(id);
        _guardValues.Add(id, value);
        return value;
    }

    private static bool OrderedCompare(
        BaseModuleProgramValue left,
        BaseModuleProgramValue right,
        string typeId,
        BaseModuleOrderedComparisonKind comparison)
    {
        if (!left.Present || !right.Present || left.IsNull || right.IsNull)
            return false;
        int order = typeId switch
        {
            "int64" => left.Value.GetInt64().CompareTo(right.Value.GetInt64()),
            "decimal" => left.Value.GetDecimal().CompareTo(right.Value.GetDecimal()),
            "dateTime" => left.Value.GetDateTimeOffset().ToUniversalTime()
                .CompareTo(right.Value.GetDateTimeOffset().ToUniversalTime()),
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
    }

    internal BaseModuleProgramValue Object(BaseModuleObjectExpression expression, CollectionDefinition? collection)
    {
        var value = new JsonObject();
        foreach (BaseModuleObjectPropertyExpression property in expression.Properties)
        {
            BaseModuleProgramValue evaluated = Evaluate(property.Value);
            if (!evaluated.Present) continue;
            string wireName = collection is null
                ? ResultWireName(property.StablePropertyId)
                : collection.Fields?.SingleOrDefault(field => string.Equals(field.Id, property.StablePropertyId, StringComparison.Ordinal))?.WireName
                    ?? throw new InvalidOperationException("base.moduleMutation.invalid");
            value.Add(wireName, JsonNode.Parse(evaluated.Value.GetRawText()));
        }
        return Parse(Encoding.UTF8.GetBytes(value.ToJsonString()));
    }

    internal TResult ProjectResult(
        BaseModuleResultProjection projection,
        IReadOnlyDictionary<string, BaseRecordMutationFact> committed,
        IReadOnlyDictionary<string, BaseModuleCommittedGeneration> generations,
        out ImmutableArray<byte> canonicalBytes)
    {
        BaseModuleProgramValue EvaluateResult(BaseModuleValueExpression expression) => expression switch
        {
            BaseModuleCommittedRecordIdExpression record => Committed(record.StatementId, static fact => JsonValue((fact.After ?? fact.Before)?.Id.Value), committed),
            BaseModuleCommittedRevisionExpression revision => Committed(revision.StatementId, static fact => JsonValue(fact.After?.Metadata.Revision?.Value), committed),
            BaseModuleCommittedUpsertDispositionExpression upsert => Committed(upsert.StatementId, static fact => JsonValue(fact.UpsertOutcome?.ToString()), committed),
            BaseModuleResultingGenerationExpression generation => generations.TryGetValue(generation.CaptureId, out BaseModuleCommittedGeneration? value)
                ? JsonValue(value.Resulting.ToCanonicalString()) : BaseModuleProgramValue.Missing,
            BaseModuleObjectExpression objectExpression => ResultObject(objectExpression, EvaluateResult),
            BaseModuleCoalesceExpression coalesce => coalesce.Values.Select(EvaluateResult).FirstOrDefault(static value => value.Present),
            BaseModuleConditionalExpression conditional => ResultConditional(conditional, EvaluateResult),
            _ => Evaluate(expression),
        };
        BaseModuleProgramValue result = ResultObject(projection.Value, EvaluateResult);
        if (!result.Present) throw new InvalidOperationException("base.moduleMutation.resultInvalid");
        TResult? typed = JsonSerializer.Deserialize(result.Value.GetRawText(), _identity.ResultTypeInfo);
        if (typed is null) throw new InvalidOperationException("base.moduleMutation.resultInvalid");
        canonicalBytes = JsonSerializer.SerializeToUtf8Bytes(typed, _identity.ResultTypeInfo).ToImmutableArray();
        return typed;
    }

    private BaseModuleProgramValue ResultObject(
        BaseModuleObjectExpression expression,
        Func<BaseModuleValueExpression, BaseModuleProgramValue> evaluate)
    {
        var value = new JsonObject();
        foreach (BaseModuleObjectPropertyExpression property in expression.Properties)
        {
            BaseModuleProgramValue evaluated = evaluate(property.Value);
            if (!evaluated.Present) continue;
            value.Add(ResultWireName(property.StablePropertyId), JsonNode.Parse(evaluated.Value.GetRawText()));
        }
        return Parse(Encoding.UTF8.GetBytes(value.ToJsonString()));
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
        JsonTypeInfo type = _identity.RequestTypeInfo;
        var path = new List<string>();
        foreach (string stableId in reference.StablePropertyPath)
        {
            path.Add(stableId);
            if (!_identity.RequestBindings.TryGetValue(string.Join('\0', path), out BaseModuleDtoPropertyBinding? binding))
                throw new InvalidOperationException("base.moduleMutation.invalid");
            JsonPropertyInfo property = type.Properties.Single(value =>
                value.AttributeProvider is MemberInfo member
                && string.Equals(member.Name, binding.ApplicationName, StringComparison.Ordinal));
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(property.Name, out current))
                return BaseModuleProgramValue.Missing;
            type = type.Options.GetTypeInfo(property.PropertyType);
        }
        return new(true, current.Clone());
    }

    private string ResultWireName(string stableId)
    {
        if (!_identity.ResultBindings.TryGetValue(stableId, out BaseModuleDtoPropertyBinding? binding))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        JsonPropertyInfo property = _identity.ResultTypeInfo.Properties.Single(value =>
            value.AttributeProvider is MemberInfo member
            && member.DeclaringType == binding.DeclaringType
            && string.Equals(member.Name, binding.ApplicationName, StringComparison.Ordinal));
        return property.Name;
    }

    private BaseModuleProgramValue CapturedField(BaseModuleCapturedFieldReference reference)
    {
        if (!_records.TryGetValue(reference.CaptureId, out BaseCapturedModuleRecord? captured) || captured.Current is null)
            return BaseModuleProgramValue.Missing;
        CollectionDefinition collection = _collections[captured.CollectionId];
        string wire = collection.Fields?.SingleOrDefault(field => string.Equals(field.Id, reference.StableFieldId, StringComparison.Ordinal))?.WireName
            ?? throw new InvalidOperationException("base.moduleMutation.invalid");
        return captured.Current.Payload.Fields?.TryGetValue(wire, out JsonElement value) == true
            ? new(true, value.Clone()) : BaseModuleProgramValue.Missing;
    }

    private BaseModuleProgramValue Generation(string captureId) =>
        _generations.TryGetValue(captureId, out BaseCapturedModuleGeneration? value) && value.Generation is not null
            ? JsonValue(value.Generation.ToCanonicalString()) : BaseModuleProgramValue.Missing;

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
            if (value.Present && !value.IsNull) return value;
        }
        return BaseModuleProgramValue.Missing;
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
        if (!left.Present || !right.Present) return BaseModuleProgramValue.Missing;
        if (expression.Operator is BaseModuleNumericOperator.IntegerAddChecked or BaseModuleNumericOperator.IntegerSubtractChecked)
        {
            long l = left.Value.GetInt64();
            long r = right.Value.GetInt64();
            return JsonValue(expression.Operator == BaseModuleNumericOperator.IntegerAddChecked ? checked(l + r) : checked(l - r));
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
        return JsonValue(result);
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
            ? project(value) : BaseModuleProgramValue.Missing;

    private BaseModuleProgramValue Record(string captureId, Func<BaseCapturedModuleRecord, BaseModuleProgramValue> project) =>
        _records.TryGetValue(captureId, out BaseCapturedModuleRecord? value) ? project(value) : BaseModuleProgramValue.Missing;

    private static BaseModuleProgramValue Committed(
        string statementId,
        Func<BaseRecordMutationFact, BaseModuleProgramValue> project,
        IReadOnlyDictionary<string, BaseRecordMutationFact> committed) =>
        committed.TryGetValue(statementId, out BaseRecordMutationFact? fact) ? project(fact) : BaseModuleProgramValue.Missing;

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

    private static BaseModuleProgramValue Parse(ReadOnlySpan<byte> bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
        return new(true, document.RootElement.Clone());
    }

    private static BaseModuleProgramValue JsonValue<T>(T value)
    {
        if (value is null) return BaseModuleProgramValue.Missing;
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
        return Parse(buffer.WrittenSpan);
    }
}
