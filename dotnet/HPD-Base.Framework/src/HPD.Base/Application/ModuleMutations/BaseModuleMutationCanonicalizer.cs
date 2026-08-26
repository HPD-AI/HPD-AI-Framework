using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Builds exactly one immutable, canonically checksummed module-mutation definition.</summary>
public sealed class BaseModuleMutationTemplateBuilder
{
    private BaseRegisteredModuleMutationDefinition? _definition;

    private BaseModuleMutationTemplateBuilder(BaseRegisteredModuleMutationDefinition definition) => _definition = definition;

    /// <summary>Starts a builder from one complete host-authored closed definition.</summary>
    public static BaseModuleMutationTemplateBuilder Create(BaseRegisteredModuleMutationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new(definition);
    }

    /// <summary>Creates a typed request-property expression from generated scalar authority.</summary>
    public static BaseModuleValue<TValue> Request<TRequest, TValue>(string nodeId, BaseModuleRequestProperty<TRequest, TValue> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return new(new BaseModuleRequestPropertyExpression
        {
            Id = nodeId,
            ResultType = property.Authority.ValueType,
            Property = new BaseModuleRequestPropertyReference { StablePropertyPath = [.. property.Authority.StablePropertyPath], Authority = property.Authority },
        });
    }

    /// <summary>Creates a typed captured-field expression from one generated collection field.</summary>
    public static BaseModuleValue<TValue> Captured<TRecord, TValue>(string nodeId, string captureId, BaseModuleCapturedField<TRecord, TValue> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var reference = new BaseModuleCapturedFieldReference { CaptureId = captureId, StableFieldId = field.Field.Id, Authority = field.Authority };
        return new(new BaseModuleCapturedFieldExpression { Id = nodeId, ResultType = field.Authority, Field = reference });
    }

    /// <summary>Creates a typed canonical constant using graph-owned scalar authority.</summary>
    public static BaseModuleValue<TValue> Constant<TValue>(string nodeId, BaseModuleConstantAuthority<TValue> authority, TValue value)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return new(new BaseModuleConstantExpression
        {
            Id = nodeId, ResultType = authority.ValueType,
            CanonicalBaseJson = BaseModuleConstantEncoder.Encode(authority.ValueType, value).ToImmutableArray(),
        });
    }

    /// <summary>Creates a typed conditional expression whose branches have identical scalar authority.</summary>
    public static BaseModuleValue<TValue> Conditional<TValue>(string nodeId, string guardId, BaseModuleValue<TValue> whenTrue, BaseModuleValue<TValue> whenFalse)
    {
        ArgumentNullException.ThrowIfNull(whenTrue); ArgumentNullException.ThrowIfNull(whenFalse);
        if (!BaseModuleValueAuthorityContract.StructurallyEquals(whenTrue.Authority, whenFalse.Authority)) throw new InvalidOperationException("base.moduleMutation.invalid");
        return new(new BaseModuleConditionalExpression { Id = nodeId, ResultType = whenTrue.Authority, GuardId = guardId, WhenTrue = whenTrue.Expression, WhenFalse = whenFalse.Expression });
    }

    /// <summary>Creates a typed missing-value coalescing expression with identical scalar authority.</summary>
    public static BaseModuleValue<TValue> Coalesce<TValue>(string nodeId, params BaseModuleValue<TValue>[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length is < 2 or > 16 || values.Any(value => value is null) || values.Any(value => !BaseModuleValueAuthorityContract.StructurallyEquals(values[0].Authority, value.Authority)))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        return new(new BaseModuleCoalesceExpression { Id = nodeId, ResultType = values[0].Authority, Values = [.. values.Select(static value => value.Expression)] });
    }

    /// <summary>Creates a typed integer expression under identical range authority.</summary>
    public static BaseModuleValue<TValue> Integer<TValue>(string nodeId, BaseModuleNumericOperator op, BaseModuleValue<TValue> left, BaseModuleValue<TValue> right)
    {
        ArgumentNullException.ThrowIfNull(left); ArgumentNullException.ThrowIfNull(right);
        if (!BaseModuleValueAuthorityContract.StructurallyEquals(left.Authority, right.Authority) || left.Authority.Kind is not (BaseModuleValueKind.Int32 or BaseModuleValueKind.Int64 or BaseModuleValueKind.UInt32 or BaseModuleValueKind.UInt64))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        return new(new BaseModuleBinaryNumericExpression { Id = nodeId, ResultType = left.Authority, Operator = op, Left = left.Expression, Right = right.Expression });
    }

    /// <summary>Creates a typed decimal expression under identical scalar authority.</summary>
    public static BaseModuleValue<decimal> Decimal(string nodeId, BaseModuleNumericOperator op, BaseModuleValue<decimal> left, BaseModuleValue<decimal> right, BaseModuleDecimalContext context)
    {
        ArgumentNullException.ThrowIfNull(left); ArgumentNullException.ThrowIfNull(right); ArgumentNullException.ThrowIfNull(context);
        if (!BaseModuleValueAuthorityContract.StructurallyEquals(left.Authority, right.Authority) || left.Authority.Kind != BaseModuleValueKind.Decimal)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        return new(new BaseModuleBinaryNumericExpression { Id = nodeId, ResultType = left.Authority, Operator = op, Left = left.Expression, Right = right.Expression, Decimal = context });
    }

    /// <summary>Creates one typed persisted-field assignment.</summary>
    public static BaseModuleFieldValue<TRecord> Field<TRecord, TValue>(BaseField<TRecord, TValue> field, BaseModuleValue<TValue> value)
    {
        ArgumentNullException.ThrowIfNull(field); ArgumentNullException.ThrowIfNull(value);
        if (!BaseModuleValueAuthorityContract.ValueCompatible(value.Authority, field.ModuleMutation.Authority)) throw new InvalidOperationException("base.moduleMutation.invalid");
        return new(new BaseModuleObjectPropertyExpression { StablePropertyId = field.Id, Value = value.Expression });
    }

    /// <summary>Creates one typed result-property projection.</summary>
    public static BaseModuleResultValue<TResult> Property<TResult, TValue>(BaseModuleResultProperty<TResult, TValue> property, BaseModuleValue<TValue> value)
    {
        ArgumentNullException.ThrowIfNull(property); ArgumentNullException.ThrowIfNull(value);
        if (!BaseModuleValueAuthorityContract.ValueCompatible(value.Authority, property.Authority.ValueType)) throw new InvalidOperationException("base.moduleMutation.invalid");
        return new(new BaseModuleObjectPropertyExpression { StablePropertyId = property.Authority.StablePropertyPath[^1], Value = value.Expression });
    }

    /// <summary>Creates one structural object from typed persisted-field assignments.</summary>
    public static BaseModuleRecordObject<TRecord> Object<TRecord>(string nodeId, params BaseModuleFieldValue<TRecord>[] fields) =>
        new(new BaseModuleObjectExpression { Id = nodeId, Properties = [.. fields.Select(static field => field.Value)] });

    /// <summary>Creates one structural result object from typed result-property projections.</summary>
    public static BaseModuleResultObject<TResult> ResultObject<TResult>(string nodeId, params BaseModuleResultValue<TResult>[] properties) =>
        new(new BaseModuleObjectExpression { Id = nodeId, Properties = [.. properties.Select(static property => property.Value)] });

    /// <summary>Captures one record using its generated collection and typed identifier authority.</summary>
    public static BaseModuleRecordCapture CaptureRecord<TRecord>(string nodeId, BaseModuleValue<BaseRecordId<TRecord>> recordId, BaseModuleCapturePresence presence) =>
        new() { Id = nodeId, CollectionId = BaseGeneratedRecordTypeContract.GetCollectionId<TRecord>(), RecordId = recordId.Expression, Presence = presence };

    /// <summary>Captures one generation cell using an optional typed string key.</summary>
    public static BaseModuleGenerationCapture CaptureGeneration(string nodeId, string cellId, BaseModuleValue<string>? key, BaseModuleGenerationAbsenceBehavior absence) =>
        new() { Id = nodeId, CellId = cellId, Key = key?.Expression, Absence = absence };

    /// <summary>Requires one captured revision to equal a typed expected value.</summary>
    public static BaseModuleRevisionEqualsGuard RevisionEquals(string nodeId, string captureId, BaseModuleValue<RevisionToken> expected) =>
        new() { Id = nodeId, CaptureId = captureId, Expected = expected.Expression };

    /// <summary>Compares one captured generated field for exact typed equality.</summary>
    public static BaseModuleFieldEqualsGuard FieldEquals<TRecord, TValue>(string nodeId, string captureId, BaseModuleCapturedField<TRecord, TValue> field, BaseModuleValue<TValue> expected) =>
        new() { Id = nodeId, Field = new BaseModuleCapturedFieldReference { CaptureId = captureId, StableFieldId = field.Field.Id, Authority = field.Authority }, Expected = expected.Expression };

    /// <summary>Compares one captured generated ordered field to a typed value.</summary>
    public static BaseModuleFieldComparisonGuard FieldCompare<TRecord, TValue>(string nodeId, string captureId, BaseModuleCapturedField<TRecord, TValue> field, BaseModuleOrderedComparisonKind comparison, BaseModuleValue<TValue> expected) =>
        new() { Id = nodeId, Field = new BaseModuleCapturedFieldReference { CaptureId = captureId, StableFieldId = field.Field.Id, Authority = field.Authority }, Comparison = comparison, Expected = expected.Expression };

    /// <summary>Tests presence for one captured generated field.</summary>
    public static BaseModuleFieldPresenceGuard FieldPresence<TRecord, TValue>(string nodeId, string captureId, BaseModuleCapturedField<TRecord, TValue> field, BaseModuleFieldPresenceTest test) =>
        new() { Id = nodeId, Field = new BaseModuleCapturedFieldReference { CaptureId = captureId, StableFieldId = field.Field.Id, Authority = field.Authority }, Test = test };

    /// <summary>Compares one captured generation to optional typed generation evidence.</summary>
    public static BaseModuleGenerationGuard Generation(string nodeId, string captureId, BaseModuleGenerationComparisonKind comparison, BaseModuleValue<BaseModuleGeneration>? expected = null) =>
        new() { Id = nodeId, CaptureId = captureId, Comparison = comparison, Expected = expected?.Expression };

    /// <summary>Creates one record using exact generated record and identifier authority.</summary>
    public static BaseModuleCreateStatement Create<TRecord>(string nodeId, BaseModuleValue<BaseRecordId<TRecord>> recordId, BaseModuleRecordObject<TRecord> payload) =>
        new() { Id = nodeId, CollectionId = BaseGeneratedRecordTypeContract.GetCollectionId<TRecord>(), RecordId = recordId.Expression, Payload = payload.Value };

    /// <summary>Patches one record using exact generated record and identifier authority.</summary>
    public static BaseModulePatchStatement Patch<TRecord>(string nodeId, BaseModuleValue<BaseRecordId<TRecord>> recordId, BaseModuleRecordObject<TRecord> patch, BaseModuleValue<RevisionToken>? expectedRevision = null) =>
        new() { Id = nodeId, CollectionId = BaseGeneratedRecordTypeContract.GetCollectionId<TRecord>(), RecordId = recordId.Expression, Patch = patch.Value, ExpectedRevision = expectedRevision?.Expression };

    /// <summary>Replaces one record using exact generated record and identifier authority.</summary>
    public static BaseModuleReplaceStatement Replace<TRecord>(string nodeId, BaseModuleValue<BaseRecordId<TRecord>> recordId, BaseModuleRecordObject<TRecord> payload, BaseModuleValue<RevisionToken>? expectedRevision = null) =>
        new() { Id = nodeId, CollectionId = BaseGeneratedRecordTypeContract.GetCollectionId<TRecord>(), RecordId = recordId.Expression, Payload = payload.Value, ExpectedRevision = expectedRevision?.Expression };

    /// <summary>Deletes one record using exact generated record and identifier authority.</summary>
    public static BaseModuleDeleteStatement Delete<TRecord>(string nodeId, BaseModuleValue<BaseRecordId<TRecord>> recordId, BaseModuleValue<RevisionToken>? expectedRevision = null) =>
        new() { Id = nodeId, CollectionId = BaseGeneratedRecordTypeContract.GetCollectionId<TRecord>(), RecordId = recordId.Expression, ExpectedRevision = expectedRevision?.Expression };

    /// <summary>Upserts one record using exact generated record and identifier authority.</summary>
    public static BaseModuleUpsertStatement Upsert<TRecord>(string nodeId, BaseModuleValue<BaseRecordId<TRecord>> recordId, BaseModuleRecordObject<TRecord> create, BaseModuleRecordObject<TRecord> update, RecordUpsertUpdateMode mode, BaseModuleValue<RevisionToken>? expectedRevision = null) =>
        new() { Id = nodeId, CollectionId = BaseGeneratedRecordTypeContract.GetCollectionId<TRecord>(), RecordId = recordId.Expression, Create = create.Value, Update = update.Value, UpdateMode = mode, ExpectedRevision = expectedRevision?.Expression };

    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleRecordCapture CaptureRecord(string id, string collectionId, BaseModuleValueExpression recordId, BaseModuleCapturePresence presence) =>
        new() { Id = id, CollectionId = collectionId, RecordId = recordId, Presence = presence };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleGenerationCapture CaptureGenerationRaw(string id, string cellId, BaseModuleValueExpression? key, BaseModuleGenerationAbsenceBehavior absence) =>
        new() { Id = id, CellId = cellId, Key = key, Absence = absence };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleRecordPresenceGuard RecordPresent(string id, string captureId, bool present) => new() { Id = id, CaptureId = captureId, MustBePresent = present };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleRevisionEqualsGuard RevisionEquals(string id, string captureId, BaseModuleValueExpression expected) => new() { Id = id, CaptureId = captureId, Expected = expected };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleFieldEqualsGuard FieldEquals(string id, BaseModuleCapturedFieldReference field, BaseModuleValueExpression expected) => new() { Id = id, Field = field, Expected = expected };
    /// <summary>Creates one exact-type ordered field guard.</summary>
    internal static BaseModuleFieldComparisonGuard FieldCompare(string id, BaseModuleCapturedFieldReference field, BaseModuleOrderedComparisonKind comparison, BaseModuleValueExpression expected) => new() { Id = id, Field = field, Comparison = comparison, Expected = expected };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleFieldPresenceGuard FieldPresence(string id, BaseModuleCapturedFieldReference field, BaseModuleFieldPresenceTest test) => new() { Id = id, Field = field, Test = test };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleGenerationGuard Generation(string id, string captureId, BaseModuleGenerationComparisonKind comparison, BaseModuleValueExpression? expected = null) => new() { Id = id, CaptureId = captureId, Comparison = comparison, Expected = expected };
    /// <summary>Creates one closed semantic-slot state guard.</summary>
    public static BaseModuleSemanticActivationStateGuard SemanticActivationState(string id, BaseModuleSemanticActivationStateTest test) => new() { Id = id, Test = test };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleLogicalGuard And(string id, params string[] guardIds) => Logical(id, BaseModuleLogicalGuardKind.And, guardIds);
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleLogicalGuard Or(string id, params string[] guardIds) => Logical(id, BaseModuleLogicalGuardKind.Or, guardIds);
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleLogicalGuard Not(string id, string guardId) => Logical(id, BaseModuleLogicalGuardKind.Not, [guardId]);
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleCreateStatement Create(string id, string collectionId, BaseModuleValueExpression recordId, BaseModuleObjectExpression payload) => new() { Id = id, CollectionId = collectionId, RecordId = recordId, Payload = payload };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModulePatchStatement Patch(string id, string collectionId, BaseModuleValueExpression recordId, BaseModuleObjectExpression patch, BaseModuleValueExpression? expectedRevision = null) => new() { Id = id, CollectionId = collectionId, RecordId = recordId, Patch = patch, ExpectedRevision = expectedRevision };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleReplaceStatement Replace(string id, string collectionId, BaseModuleValueExpression recordId, BaseModuleObjectExpression payload, BaseModuleValueExpression? expectedRevision = null) => new() { Id = id, CollectionId = collectionId, RecordId = recordId, Payload = payload, ExpectedRevision = expectedRevision };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleDeleteStatement Delete(string id, string collectionId, BaseModuleValueExpression recordId, BaseModuleValueExpression? expectedRevision = null) => new() { Id = id, CollectionId = collectionId, RecordId = recordId, ExpectedRevision = expectedRevision };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleUpsertStatement Upsert(string id, string collectionId, BaseModuleValueExpression recordId, BaseModuleObjectExpression create, BaseModuleObjectExpression update, RecordUpsertUpdateMode mode, BaseModuleValueExpression? expectedRevision = null) => new() { Id = id, CollectionId = collectionId, RecordId = recordId, Create = create, Update = update, UpdateMode = mode, ExpectedRevision = expectedRevision };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleIncrementGenerationStatement IncrementGeneration(string id, string captureId, bool createIfAbsent) => new() { Id = id, CaptureId = captureId, CreateIfAbsent = createIfAbsent };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleIfStatement If(string id, string guardId, BaseModuleMutationBlock whenTrue, BaseModuleMutationBlock whenFalse) => new() { Id = id, GuardId = guardId, WhenTrue = whenTrue, WhenFalse = whenFalse };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleRequireStatement Require(string id, string guardId, string requirementId) => new() { Id = id, GuardId = guardId, RequirementId = requirementId };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleRequestPropertyExpression RequestProperty(string id, BaseModuleRequestPropertyReference property) => new() { Id = id, ResultType = property.Authority.ValueType, Property = property };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleConstantExpression Constant(string id, BaseModuleValueType resultType, ReadOnlySpan<byte> canonicalBaseJson) => new() { Id = id, ResultType = resultType, CanonicalBaseJson = canonicalBaseJson.ToArray().ToImmutableArray() };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleCapturedFieldExpression CapturedField(string id, BaseModuleCapturedFieldReference field) => new() { Id = id, ResultType = field.Authority, Field = field };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleCapturedRecordIdExpression CapturedRecordId(string id, BaseModuleValueType resultType, string captureId) => new() { Id = id, ResultType = resultType, CaptureId = captureId };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleCapturedRevisionExpression CapturedRevisionRaw(string id, string captureId) => new() { Id = id, ResultType = BaseModuleValueAuthorityContract.Primitive<RevisionToken>(), CaptureId = captureId };
    /// <summary>Creates a typed captured-revision expression.</summary>
    public static BaseModuleValue<RevisionToken> CapturedRevision(string nodeId, string captureId) =>
        new(new BaseModuleCapturedRevisionExpression { Id = nodeId, ResultType = BaseModuleValueAuthorityContract.Primitive<RevisionToken>(), CaptureId = captureId });
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleCapturedGenerationExpression CapturedGenerationRaw(string id, string captureId) => new() { Id = id, ResultType = BaseModuleValueAuthorityContract.Primitive<BaseModuleGeneration>(), CaptureId = captureId };
    /// <summary>Creates one typed captured-generation expression.</summary>
    public static BaseModuleValue<BaseModuleGeneration> CapturedGeneration(string nodeId, string captureId) =>
        new(new BaseModuleCapturedGenerationExpression { Id = nodeId, ResultType = BaseModuleValueAuthorityContract.Primitive<BaseModuleGeneration>(), CaptureId = captureId });
    /// <summary>Creates a typed captured record-ID expression for the exact generated record type.</summary>
    public static BaseModuleValue<BaseRecordId<TRecord>> CapturedRecordId<TRecord>(string nodeId, string captureId) =>
        new(new BaseModuleCapturedRecordIdExpression { Id = nodeId, ResultType = BaseModuleValueAuthorityContract.RecordId<TRecord>(), CaptureId = captureId });
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleCommittedRecordIdExpression CommittedRecordId(string id, BaseModuleValueType resultType, string statementId) => new() { Id = id, ResultType = resultType, StatementId = statementId };
    /// <summary>Creates a typed committed record-ID expression for the exact generated record type.</summary>
    public static BaseModuleValue<BaseRecordId<TRecord>> CommittedRecordId<TRecord>(string nodeId, string statementId) =>
        new(new BaseModuleCommittedRecordIdExpression { Id = nodeId, ResultType = BaseModuleValueAuthorityContract.RecordId<TRecord>(), StatementId = statementId });
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleCommittedRevisionExpression CommittedRevisionRaw(string id, string statementId) => new() { Id = id, ResultType = BaseModuleValueAuthorityContract.Primitive<RevisionToken>(), StatementId = statementId };
    /// <summary>Creates a typed committed-revision expression.</summary>
    public static BaseModuleValue<RevisionToken> CommittedRevision(string nodeId, string statementId) =>
        new(new BaseModuleCommittedRevisionExpression { Id = nodeId, ResultType = BaseModuleValueAuthorityContract.Primitive<RevisionToken>(), StatementId = statementId });
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleCommittedUpsertDispositionExpression CommittedUpsertDisposition(string id, BaseModuleValueType resultType, string statementId) => new() { Id = id, ResultType = resultType, StatementId = statementId };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleResultingGenerationExpression ResultingGenerationRaw(string id, string captureId) => new() { Id = id, ResultType = BaseModuleValueAuthorityContract.Primitive<BaseModuleGeneration>(), CaptureId = captureId };
    /// <summary>Creates one typed resulting-generation expression.</summary>
    public static BaseModuleValue<BaseModuleGeneration> ResultingGeneration(string nodeId, string captureId) =>
        new(new BaseModuleResultingGenerationExpression { Id = nodeId, ResultType = BaseModuleValueAuthorityContract.Primitive<BaseModuleGeneration>(), CaptureId = captureId });
    /// <summary>Projects the semantic ensure disposition.</summary>
    internal static BaseModuleSemanticActivationDispositionExpression SemanticActivationDisposition(string id, BaseModuleValueType resultType) => new() { Id = id, ResultType = resultType };
    /// <summary>Projects the live semantic activation ID.</summary>
    internal static BaseModuleSemanticActivationIdExpression SemanticActivationId(string id, BaseModuleValueType resultType) => new() { Id = id, ResultType = resultType };
    /// <summary>Projects whether ensure created the activation.</summary>
    internal static BaseModuleSemanticActivationWasMaterializedExpression SemanticActivationWasMaterialized(string id) => new() { Id = id, ResultType = BaseModuleValueAuthorityContract.Primitive<bool>() };
    /// <summary>Projects the semantic retirement disposition.</summary>
    internal static BaseModuleSemanticActivationRetirementDispositionExpression SemanticActivationRetirementDisposition(string id, BaseModuleValueType resultType) => new() { Id = id, ResultType = resultType };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleCoalesceExpression Coalesce(string id, BaseModuleValueType resultType, params BaseModuleValueExpression[] values) => new() { Id = id, ResultType = resultType, Values = [.. values] };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleConditionalExpression Conditional(string id, BaseModuleValueType resultType, string guardId, BaseModuleValueExpression whenTrue, BaseModuleValueExpression whenFalse) => new() { Id = id, ResultType = resultType, GuardId = guardId, WhenTrue = whenTrue, WhenFalse = whenFalse };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleBinaryNumericExpression Numeric(string id, BaseModuleValueType resultType, BaseModuleNumericOperator op, BaseModuleValueExpression left, BaseModuleValueExpression right, BaseModuleDecimalContext? decimalContext = null) => new() { Id = id, ResultType = resultType, Operator = op, Left = left, Right = right, Decimal = decimalContext };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    internal static BaseModuleObjectExpression Object(string id, params BaseModuleObjectPropertyExpression[] properties) => new() { Id = id, Properties = [.. properties] };
    /// <summary>Creates one closed graph-owned object-property node.</summary>
    internal static BaseModuleObjectPropertyExpression Property(string propertyId, BaseModuleValueExpression value) => new() { StablePropertyId = propertyId, Value = value };
    /// <summary>Creates one closed ordered statement block.</summary>
    public static BaseModuleMutationBlock Block(params BaseModuleStatement[] statements) => new() { Statements = [.. statements] };
    /// <summary>Creates one closed result projection.</summary>
    public static BaseModuleResultProjection Result<TResult>(BaseModuleResultObject<TResult> value) => new() { Value = value.Value };
    internal static BaseModuleResultProjection ResultRaw(BaseModuleObjectExpression value) => new() { Value = value };

    private static BaseModuleLogicalGuard Logical(string id, BaseModuleLogicalGuardKind kind, string[] children) => new() { Id = id, Kind = kind, ChildGuardIds = [.. children] };

    /// <summary>Freezes the definition and assigns its sole canonical checksum.</summary>
    public BaseRegisteredModuleMutationDefinition Build()
    {
        BaseRegisteredModuleMutationDefinition definition = Interlocked.Exchange(ref _definition, null)
            ?? throw new InvalidOperationException("base.moduleMutation.invalid");
        return BaseModuleMutationContract.Seal(definition);
    }
}

/// <summary>Provides the single canonical identity authority for registered module mutations.</summary>
public static class BaseModuleMutationContract
{
    /// <summary>Computes one module generation-cell definition checksum.</summary>
    public static string ComputeCellChecksum(BaseModuleGenerationCellDefinition cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var writer = new CanonicalWriter(hash);
        writer.String("base.moduleMutation.cell.v1"); writer.String(cell.Id); writer.Integer(cell.Version);
        writer.String(cell.OwningModuleId); writer.Integer((int)cell.Scope);
        writer.Integer(cell.MaximumKeyUtf8Bytes); writer.Integer(cell.MaximumCellsPerOperation);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    /// <summary>Returns a deeply equivalent definition carrying its computed canonical checksum.</summary>
    public static BaseRegisteredModuleMutationDefinition Seal(BaseRegisteredModuleMutationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition with { Checksum = ComputeChecksum(definition) };
    }

    /// <summary>Computes the normative <c>base.moduleMutation.template.v1</c> checksum.</summary>
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleMutationChecksum ComputeChecksum(BaseRegisteredModuleMutationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var writer = new CanonicalWriter(hash);
        writer.Marker("base.moduleMutation.template.v1");
        writer.String(definition.Id); writer.Integer(definition.Version); writer.String(definition.OwningModuleId);
        writer.String(definition.GrantId); writer.Integer((int)definition.Audience);
        writer.String(definition.RequestTypeId); writer.String(definition.ResultTypeId);
        writer.StringSet(definition.SystemCollectionIds); writer.StringSet(definition.GenerationCellIds);
        writer.Count(definition.SystemSourceGrants.Length);
        foreach (BaseModuleSystemSourceGrant source in definition.SystemSourceGrants)
        { writer.String(source.CollectionId); writer.String(source.GrantId); }
        writer.StringSet(definition.ImportedSubjectContractIds);
        writer.Template(definition.Template); writer.Limits(definition.Limits);
        writer.Integer(definition.ReceiptPolicy.FormatVersion); writer.Integer(definition.ReceiptPolicy.Lifetime.Ticks);
        return BaseModuleMutationChecksum.Create(hash.GetHashAndReset());
    }

    private sealed class CanonicalWriter(IncrementalHash hash)
    {
        private IReadOnlyDictionary<string, BaseModuleGuard>? _guards;
        private HashSet<string>? _digestStack;
        internal void Marker(string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            hash.AppendData(bytes);
            hash.AppendData([0]);
        }

        internal void Count(int value)
        {
            if (value < 0) throw new InvalidOperationException("base.moduleMutation.invalid");
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value); hash.AppendData(bytes);
        }

        internal void Discriminator(byte value) => hash.AppendData([value]);

        internal void Integer(long value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes);
        }

        internal void Boolean(bool value) => hash.AppendData(value ? [1] : [0]);

        internal void String(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!value.IsNormalized(NormalizationForm.FormC) || value.Any(char.IsControl))
                throw new InvalidOperationException("base.moduleMutation.invalid");
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Bytes(bytes);
        }

        private void NullableString(string? value)
        {
            Boolean(value is not null); if (value is not null) String(value);
        }

        private void Bytes(ReadOnlySpan<byte> value)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
            hash.AppendData(length); hash.AppendData(value);
        }

        internal void StringSet(ImmutableArray<string> values)
        {
            if (!values.SequenceEqual(values.Order(StringComparer.Ordinal), StringComparer.Ordinal)
                || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new InvalidOperationException("base.moduleMutation.invalid");
            Count(values.Length);
            foreach (string value in values) String(value);
        }

        internal void Limits(BaseModuleMutationLimits value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Integer(value.MaximumCaptures); Integer(value.MaximumRecordCaptures); Integer(value.MaximumRelationTargetCaptures);
            Integer(value.MaximumGenerationCaptures); Integer(value.MaximumRecordMutations); Integer(value.MaximumGenerationReads);
            Integer(value.MaximumGenerationComparisons); Integer(value.MaximumGenerationIncrements); Integer(value.MaximumGuardNodes);
            Integer(value.MaximumGuardDepth); Integer(value.MaximumStatements); Integer(value.MaximumBranches);
            Integer(value.MaximumExpressionNodes); Integer(value.MaximumReadIntervals); Integer(value.MaximumSubjectValidations);
            Integer(value.MaximumAuthorityReads); Integer(value.MaximumRelationChecks); Integer(value.MaximumUniqueConstraintChecks);
            Integer(value.MaximumRequestBytes); Integer(value.MaximumSelectedBytes); Integer(value.MaximumGenerationBytes);
            Integer(value.MaximumEvidenceBytes); Integer(value.MaximumWrittenBytes); Integer(value.MaximumFactBytes);
            Integer(value.MaximumJournalBytes); Integer(value.MaximumReceiptBytes); Integer(value.MaximumResultBytes);
            Integer(value.MaximumTransientBytes);
            Integer(value.Deadlines.AcquisitionTimeout.Ticks); Integer(value.Deadlines.TransactionTimeout.Ticks);
            Integer(value.Deadlines.CommitObservationTimeout.Ticks); Integer(value.Deadlines.ReceiptResolutionTimeout.Ticks);
        }

        internal void Template(BaseModuleMutationTemplate value)
        {
            if (!value.Captures.SequenceEqual(value.Captures.OrderBy(static item => item.Id, StringComparer.Ordinal))
                || !value.Guards.SequenceEqual(value.Guards.OrderBy(static item => item.Id, StringComparer.Ordinal)))
                throw new InvalidOperationException("base.moduleMutation.invalid");
            _guards = value.Guards.ToDictionary(static guard => guard.Id, StringComparer.Ordinal);
            _digestStack = new HashSet<string>(StringComparer.Ordinal);
            Count(value.Captures.Length);
            foreach (BaseModuleCapture capture in value.Captures) Capture(capture);
            Count(value.Guards.Length);
            foreach (BaseModuleGuard guard in value.Guards) Guard(guard);
            Block(value.Body); Object(value.Result.Value);
        }

        private void Capture(BaseModuleCapture value)
        {
            switch (value)
            {
                case BaseModuleRecordCapture record:
                    Discriminator(0); String(record.Id); String(record.CollectionId); Expression(record.RecordId); Integer((int)record.Presence); break;
                case BaseModuleGenerationCapture generation:
                    Discriminator(1); String(generation.Id); String(generation.CellId); Boolean(generation.Key is not null);
                    if (generation.Key is not null) Expression(generation.Key); Integer((int)generation.Absence); break;
                default: throw new InvalidOperationException("base.moduleMutation.invalid");
            }
        }

        private void Guard(BaseModuleGuard value)
        {
            String(value.Id);
            switch (value)
            {
                case BaseModuleRecordPresenceGuard guard:
                    Discriminator(0); String(guard.CaptureId); Boolean(guard.MustBePresent); break;
                case BaseModuleRevisionEqualsGuard guard:
                    Discriminator(1); String(guard.CaptureId); Expression(guard.Expected); break;
                case BaseModuleFieldEqualsGuard guard:
                    Discriminator(2); Field(guard.Field); Expression(guard.Expected); break;
                case BaseModuleFieldComparisonGuard guard:
                    Discriminator(6); Field(guard.Field); Discriminator((byte)guard.Comparison); Expression(guard.Expected); break;
                case BaseModuleFieldPresenceGuard guard:
                    Discriminator(3); Field(guard.Field); Integer((int)guard.Test); break;
                case BaseModuleGenerationGuard guard:
                    Discriminator(4); String(guard.CaptureId); Integer((int)guard.Comparison); Boolean(guard.Expected is not null);
                    if (guard.Expected is not null) Expression(guard.Expected); break;
                case BaseModuleLogicalGuard guard:
                    Discriminator(5); Integer((int)guard.Kind);
                    if (guard.Kind is BaseModuleLogicalGuardKind.And or BaseModuleLogicalGuardKind.Or)
                    {
                        string[] digestKeys = guard.ChildGuardIds.Select(id => Convert.ToHexString(GuardDigest(id))).ToArray();
                        if (digestKeys.Distinct(StringComparer.Ordinal).Count() != digestKeys.Length)
                            throw new InvalidOperationException("base.moduleMutation.invalid");
                    }
                    string[] materialized = guard.ChildGuardIds.ToArray();
                    if (guard.Kind is BaseModuleLogicalGuardKind.And or BaseModuleLogicalGuardKind.Or)
                    {
                        string[] canonical = materialized.OrderBy(GuardDigest, ByteArrayComparer.Instance).ToArray();
                        if (!materialized.SequenceEqual(canonical, StringComparer.Ordinal))
                            throw new InvalidOperationException("base.moduleMutation.invalid");
                    }
                    Count(materialized.Length);
                    foreach (string child in materialized) String(child); break;
                case BaseModuleSemanticActivationStateGuard guard:
                    Discriminator(7); Integer((int)guard.Test); break;
                default: throw new InvalidOperationException("base.moduleMutation.invalid");
            }
        }

        private void Block(BaseModuleMutationBlock value)
        {
            Count(value.Statements.Length); foreach (BaseModuleStatement statement in value.Statements) Statement(statement);
        }

        private void Statement(BaseModuleStatement value)
        {
            String(value.Id);
            switch (value)
            {
                case BaseModuleCreateStatement statement:
                    Discriminator(0); String(statement.CollectionId); Expression(statement.RecordId); Object(statement.Payload); break;
                case BaseModulePatchStatement statement:
                    Discriminator(1); String(statement.CollectionId); Expression(statement.RecordId); Object(statement.Patch); Optional(statement.ExpectedRevision); break;
                case BaseModuleReplaceStatement statement:
                    Discriminator(2); String(statement.CollectionId); Expression(statement.RecordId); Object(statement.Payload); Optional(statement.ExpectedRevision); break;
                case BaseModuleDeleteStatement statement:
                    Discriminator(3); String(statement.CollectionId); Expression(statement.RecordId); Optional(statement.ExpectedRevision); break;
                case BaseModuleUpsertStatement statement:
                    Discriminator(4); String(statement.CollectionId); Expression(statement.RecordId); Object(statement.Create); Object(statement.Update);
                    Integer((int)statement.UpdateMode); Optional(statement.ExpectedRevision); break;
                case BaseModuleIncrementGenerationStatement statement:
                    Discriminator(5); String(statement.CaptureId); Boolean(statement.CreateIfAbsent); break;
                case BaseModuleIfStatement statement:
                    Discriminator(6); String(statement.GuardId); Block(statement.WhenTrue); Block(statement.WhenFalse); break;
                case BaseModuleRequireStatement statement:
                    Discriminator(7); String(statement.GuardId); String(statement.RequirementId); break;
                default: throw new InvalidOperationException("base.moduleMutation.invalid");
            }
        }

        private void Optional(BaseModuleValueExpression? value)
        {
            Boolean(value is not null); if (value is not null) Expression(value);
        }

        private void Expression(BaseModuleValueExpression value)
        {
            String(value.Id);
            if (value.ResultType is null) throw new InvalidOperationException("base.moduleMutation.invalid");
            ValueType(value.ResultType);
            switch (value)
            {
                case BaseModuleRequestPropertyExpression expression:
                    Discriminator(0); Count(expression.Property.StablePropertyPath.Length);
                    foreach (string edge in expression.Property.StablePropertyPath) String(edge);
                    Bytes(expression.Property.Authority.AuthorityChecksum.ToArray()); break;
                case BaseModuleConstantExpression expression: Discriminator(1); Bytes(expression.CanonicalBaseJson.AsSpan()); break;
                case BaseModuleCapturedRecordIdExpression expression: Discriminator(2); String(expression.CaptureId); break;
                case BaseModuleCapturedRevisionExpression expression: Discriminator(3); String(expression.CaptureId); break;
                case BaseModuleCapturedFieldExpression expression: Discriminator(4); Field(expression.Field); break;
                case BaseModuleCapturedGenerationExpression expression: Discriminator(5); String(expression.CaptureId); break;
                case BaseModuleCommittedRecordIdExpression expression: Discriminator(6); String(expression.StatementId); break;
                case BaseModuleCommittedRevisionExpression expression: Discriminator(7); String(expression.StatementId); break;
                case BaseModuleCommittedUpsertDispositionExpression expression: Discriminator(8); String(expression.StatementId); break;
                case BaseModuleResultingGenerationExpression expression: Discriminator(9); String(expression.CaptureId); break;
                case BaseModuleCoalesceExpression expression:
                    Discriminator(10); Count(expression.Values.Length); foreach (BaseModuleValueExpression child in expression.Values) Expression(child); break;
                case BaseModuleConditionalExpression expression:
                    Discriminator(11); String(expression.GuardId); Expression(expression.WhenTrue); Expression(expression.WhenFalse); break;
                case BaseModuleBinaryNumericExpression expression:
                    Discriminator(12); Integer((int)expression.Operator); Expression(expression.Left); Expression(expression.Right);
                    Boolean(expression.Decimal is not null);
                    if (expression.Decimal is { } context)
                    { Integer(context.Precision); Integer(context.Scale); Integer((int)context.Rounding); }
                    break;
                case BaseModuleSemanticActivationDispositionExpression: Discriminator(13); break;
                case BaseModuleSemanticActivationIdExpression: Discriminator(14); break;
                case BaseModuleSemanticActivationWasMaterializedExpression: Discriminator(15); break;
                case BaseModuleSemanticActivationRetirementDispositionExpression: Discriminator(16); break;
                default: throw new InvalidOperationException("base.moduleMutation.invalid");
            }
        }

        private void Object(BaseModuleObjectExpression expression)
        {
            String(expression.Id); Count(expression.Properties.Length);
            foreach (BaseModuleObjectPropertyExpression property in expression.Properties)
            { String(property.StablePropertyId); Expression(property.Value); }
        }

        private void Field(BaseModuleCapturedFieldReference value)
        {
            String(value.CaptureId); String(value.StableFieldId);
            ValueType(value.Authority);
        }

        private void ValueType(BaseModuleValueType value)
        {
            var writer = new System.Buffers.ArrayBufferWriter<byte>();
            BaseSchemaContract.WriteModuleValueType(writer, value);
            Bytes(writer.WrittenSpan);
        }

        private byte[] GuardDigest(string id)
        {
            if (_guards is null || !_guards.TryGetValue(id, out BaseModuleGuard? guard)
                || _digestStack is null || !_digestStack.Add(id))
                throw new InvalidOperationException("base.moduleMutation.invalid");
            try
            {
                using IncrementalHash childHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var child = new CanonicalWriter(childHash) { _guards = _guards, _digestStack = _digestStack };
                child.Guard(guard);
                return childHash.GetHashAndReset();
            }
            finally { _digestStack.Remove(id); }
        }

        private sealed class ByteArrayComparer : IComparer<byte[]>
        {
            internal static ByteArrayComparer Instance { get; } = new();
            public int Compare(byte[]? left, byte[]? right) => left.AsSpan().SequenceCompareTo(right);
        }
    }
}
