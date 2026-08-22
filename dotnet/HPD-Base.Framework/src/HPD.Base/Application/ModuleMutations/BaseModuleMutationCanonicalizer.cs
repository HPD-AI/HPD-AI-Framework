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

    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleRecordCapture CaptureRecord(string id, string collectionId, BaseModuleValueExpression recordId, BaseModuleCapturePresence presence) =>
        new() { Id = id, CollectionId = collectionId, RecordId = recordId, Presence = presence };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleGenerationCapture CaptureGeneration(string id, string cellId, BaseModuleValueExpression? key, BaseModuleGenerationAbsenceBehavior absence) =>
        new() { Id = id, CellId = cellId, Key = key, Absence = absence };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleRecordPresenceGuard RecordPresent(string id, string captureId, bool present) => new() { Id = id, CaptureId = captureId, MustBePresent = present };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleRevisionEqualsGuard RevisionEquals(string id, string captureId, BaseModuleValueExpression expected) => new() { Id = id, CaptureId = captureId, Expected = expected };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleFieldEqualsGuard FieldEquals(string id, BaseModuleCapturedFieldReference field, BaseModuleValueExpression expected) => new() { Id = id, Field = field, Expected = expected };
    /// <summary>Creates one exact-type ordered field guard.</summary>
    public static BaseModuleFieldComparisonGuard FieldCompare(string id, BaseModuleCapturedFieldReference field, BaseModuleOrderedComparisonKind comparison, BaseModuleValueExpression expected) => new() { Id = id, Field = field, Comparison = comparison, Expected = expected };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleFieldPresenceGuard FieldPresence(string id, BaseModuleCapturedFieldReference field, BaseModuleFieldPresenceTest test) => new() { Id = id, Field = field, Test = test };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleGenerationGuard Generation(string id, string captureId, BaseModuleGenerationComparisonKind comparison, BaseModuleValueExpression? expected = null) => new() { Id = id, CaptureId = captureId, Comparison = comparison, Expected = expected };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleLogicalGuard And(string id, params string[] guardIds) => Logical(id, BaseModuleLogicalGuardKind.And, guardIds);
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleLogicalGuard Or(string id, params string[] guardIds) => Logical(id, BaseModuleLogicalGuardKind.Or, guardIds);
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleLogicalGuard Not(string id, string guardId) => Logical(id, BaseModuleLogicalGuardKind.Not, [guardId]);
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleCreateStatement Create(string id, string collectionId, BaseModuleValueExpression recordId, BaseModuleObjectExpression payload) => new() { Id = id, CollectionId = collectionId, RecordId = recordId, Payload = payload };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModulePatchStatement Patch(string id, string collectionId, BaseModuleValueExpression recordId, BaseModuleObjectExpression patch, BaseModuleValueExpression? expectedRevision = null) => new() { Id = id, CollectionId = collectionId, RecordId = recordId, Patch = patch, ExpectedRevision = expectedRevision };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleReplaceStatement Replace(string id, string collectionId, BaseModuleValueExpression recordId, BaseModuleObjectExpression payload, BaseModuleValueExpression? expectedRevision = null) => new() { Id = id, CollectionId = collectionId, RecordId = recordId, Payload = payload, ExpectedRevision = expectedRevision };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleDeleteStatement Delete(string id, string collectionId, BaseModuleValueExpression recordId, BaseModuleValueExpression? expectedRevision = null) => new() { Id = id, CollectionId = collectionId, RecordId = recordId, ExpectedRevision = expectedRevision };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleUpsertStatement Upsert(string id, string collectionId, BaseModuleValueExpression recordId, BaseModuleObjectExpression create, BaseModuleObjectExpression update, RecordUpsertUpdateMode mode, BaseModuleValueExpression? expectedRevision = null) => new() { Id = id, CollectionId = collectionId, RecordId = recordId, Create = create, Update = update, UpdateMode = mode, ExpectedRevision = expectedRevision };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleIncrementGenerationStatement IncrementGeneration(string id, string captureId, bool createIfAbsent) => new() { Id = id, CaptureId = captureId, CreateIfAbsent = createIfAbsent };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleIfStatement If(string id, string guardId, BaseModuleMutationBlock whenTrue, BaseModuleMutationBlock whenFalse) => new() { Id = id, GuardId = guardId, WhenTrue = whenTrue, WhenFalse = whenFalse };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleRequireStatement Require(string id, string guardId, string requirementId) => new() { Id = id, GuardId = guardId, RequirementId = requirementId };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleRequestPropertyExpression RequestProperty(string id, string resultTypeId, BaseModuleRequestPropertyReference property) => new() { Id = id, ResultTypeId = resultTypeId, Property = property };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleConstantExpression Constant(string id, string resultTypeId, ReadOnlySpan<byte> canonicalBaseJson) => new() { Id = id, ResultTypeId = resultTypeId, CanonicalBaseJson = canonicalBaseJson.ToArray().ToImmutableArray() };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleCapturedFieldExpression CapturedField(string id, string resultTypeId, BaseModuleCapturedFieldReference field) => new() { Id = id, ResultTypeId = resultTypeId, Field = field };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleCapturedRecordIdExpression CapturedRecordId(string id, string resultTypeId, string captureId) => new() { Id = id, ResultTypeId = resultTypeId, CaptureId = captureId };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleCapturedRevisionExpression CapturedRevision(string id, string captureId) => new() { Id = id, ResultTypeId = "revision", CaptureId = captureId };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleCapturedGenerationExpression CapturedGeneration(string id, string resultTypeId, string captureId) => new() { Id = id, ResultTypeId = resultTypeId, CaptureId = captureId };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleCommittedRecordIdExpression CommittedRecordId(string id, string resultTypeId, string statementId) => new() { Id = id, ResultTypeId = resultTypeId, StatementId = statementId };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleCommittedRevisionExpression CommittedRevision(string id, string statementId) => new() { Id = id, ResultTypeId = "revision", StatementId = statementId };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleCommittedUpsertDispositionExpression CommittedUpsertDisposition(string id, string resultTypeId, string statementId) => new() { Id = id, ResultTypeId = resultTypeId, StatementId = statementId };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleResultingGenerationExpression ResultingGeneration(string id, string resultTypeId, string captureId) => new() { Id = id, ResultTypeId = resultTypeId, CaptureId = captureId };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleCoalesceExpression Coalesce(string id, string resultTypeId, params BaseModuleValueExpression[] values) => new() { Id = id, ResultTypeId = resultTypeId, Values = [.. values] };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleConditionalExpression Conditional(string id, string resultTypeId, string guardId, BaseModuleValueExpression whenTrue, BaseModuleValueExpression whenFalse) => new() { Id = id, ResultTypeId = resultTypeId, GuardId = guardId, WhenTrue = whenTrue, WhenFalse = whenFalse };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleBinaryNumericExpression Numeric(string id, string resultTypeId, BaseModuleNumericOperator op, BaseModuleValueExpression left, BaseModuleValueExpression right, BaseModuleDecimalContext? decimalContext = null) => new() { Id = id, ResultTypeId = resultTypeId, Operator = op, Left = left, Right = right, Decimal = decimalContext };
    /// <summary>Creates one closed graph-owned module-mutation node.</summary>
    public static BaseModuleObjectExpression Object(string id, string resultTypeId, params BaseModuleObjectPropertyExpression[] properties) => new() { Id = id, ResultTypeId = resultTypeId, Properties = [.. properties] };
    /// <summary>Creates one closed graph-owned object-property node.</summary>
    public static BaseModuleObjectPropertyExpression Property(string propertyId, BaseModuleValueExpression value) => new() { StablePropertyId = propertyId, Value = value };
    /// <summary>Creates one closed ordered statement block.</summary>
    public static BaseModuleMutationBlock Block(params BaseModuleStatement[] statements) => new() { Statements = [.. statements] };
    /// <summary>Creates one closed result projection.</summary>
    public static BaseModuleResultProjection Result(BaseModuleObjectExpression value) => new() { Value = value };

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
            Block(value.Body); Expression(value.Result.Value);
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
                    Discriminator(0); String(statement.CollectionId); Expression(statement.RecordId); Expression(statement.Payload); break;
                case BaseModulePatchStatement statement:
                    Discriminator(1); String(statement.CollectionId); Expression(statement.RecordId); Expression(statement.Patch); Optional(statement.ExpectedRevision); break;
                case BaseModuleReplaceStatement statement:
                    Discriminator(2); String(statement.CollectionId); Expression(statement.RecordId); Expression(statement.Payload); Optional(statement.ExpectedRevision); break;
                case BaseModuleDeleteStatement statement:
                    Discriminator(3); String(statement.CollectionId); Expression(statement.RecordId); Optional(statement.ExpectedRevision); break;
                case BaseModuleUpsertStatement statement:
                    Discriminator(4); String(statement.CollectionId); Expression(statement.RecordId); Expression(statement.Create); Expression(statement.Update);
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
            String(value.Id); String(value.ResultTypeId);
            switch (value)
            {
                case BaseModuleRequestPropertyExpression expression:
                    Discriminator(0); Count(expression.Property.StablePropertyPath.Length);
                    foreach (string edge in expression.Property.StablePropertyPath) String(edge);
                    String(expression.Property.DeclaredTypeId); break;
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
                case BaseModuleObjectExpression expression:
                    Discriminator(13); Count(expression.Properties.Length);
                    foreach (BaseModuleObjectPropertyExpression property in expression.Properties)
                    { String(property.StablePropertyId); Expression(property.Value); }
                    break;
                default: throw new InvalidOperationException("base.moduleMutation.invalid");
            }
        }

        private void Field(BaseModuleCapturedFieldReference value)
        {
            String(value.CaptureId); String(value.StableFieldId); String(value.DeclaredTypeId);
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
