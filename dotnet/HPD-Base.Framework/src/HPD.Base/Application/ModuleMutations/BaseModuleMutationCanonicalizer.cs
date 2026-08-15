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
    public static BaseModuleMutationChecksum ComputeChecksum(BaseRegisteredModuleMutationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var writer = new CanonicalWriter(hash);
        writer.String("base.moduleMutation.template.v1");
        writer.String(definition.Id); writer.Integer(definition.Version); writer.String(definition.OwningModuleId);
        writer.String(definition.GrantId); writer.Integer((int)definition.Audience);
        writer.String(definition.RequestTypeId); writer.String(definition.ResultTypeId);
        writer.StringSet(definition.SystemCollectionIds); writer.StringSet(definition.GenerationCellIds);
        writer.StringSet(definition.ImportedSubjectContractIds);
        writer.Template(definition.Template); writer.Limits(definition.Limits);
        writer.Integer(definition.ReceiptPolicy.FormatVersion); writer.Integer(definition.ReceiptPolicy.Lifetime.Ticks);
        return BaseModuleMutationChecksum.Create(hash.GetHashAndReset());
    }

    private sealed class CanonicalWriter(IncrementalHash hash)
    {
        internal void Integer(long value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes);
        }

        internal void Boolean(bool value) => hash.AppendData(value ? [1] : [0]);

        internal void String(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            byte[] bytes = Encoding.UTF8.GetBytes(value.Normalize(NormalizationForm.FormC));
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
            Integer(values.Length);
            foreach (string value in values.Order(StringComparer.Ordinal)) String(value);
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
            Integer(value.Captures.Length);
            foreach (BaseModuleCapture capture in value.Captures.OrderBy(static item => item.Id, StringComparer.Ordinal)) Capture(capture);
            Integer(value.Guards.Length);
            foreach (BaseModuleGuard guard in value.Guards.OrderBy(static item => item.Id, StringComparer.Ordinal)) Guard(guard);
            Block(value.Body); Expression(value.Result.Value);
        }

        private void Capture(BaseModuleCapture value)
        {
            switch (value)
            {
                case BaseModuleRecordCapture record:
                    Integer(0); String(record.Id); String(record.CollectionId); Expression(record.RecordId); Integer((int)record.Presence); break;
                case BaseModuleGenerationCapture generation:
                    Integer(1); String(generation.Id); String(generation.CellId); Boolean(generation.Key is not null);
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
                    Integer(0); String(guard.CaptureId); Boolean(guard.MustBePresent); break;
                case BaseModuleRevisionEqualsGuard guard:
                    Integer(1); String(guard.CaptureId); Expression(guard.Expected); break;
                case BaseModuleFieldEqualsGuard guard:
                    Integer(2); Field(guard.Field); Expression(guard.Expected); break;
                case BaseModuleFieldPresenceGuard guard:
                    Integer(3); Field(guard.Field); Integer((int)guard.Test); break;
                case BaseModuleGenerationGuard guard:
                    Integer(4); String(guard.CaptureId); Integer((int)guard.Comparison); Boolean(guard.Expected is not null);
                    if (guard.Expected is not null) Expression(guard.Expected); break;
                case BaseModuleLogicalGuard guard:
                    Integer(5); Integer((int)guard.Kind);
                    IEnumerable<string> children = guard.Kind is BaseModuleLogicalGuardKind.And or BaseModuleLogicalGuardKind.Or
                        ? guard.ChildGuardIds.Order(StringComparer.Ordinal) : guard.ChildGuardIds;
                    string[] materialized = children.ToArray(); Integer(materialized.Length);
                    foreach (string child in materialized) String(child); break;
                default: throw new InvalidOperationException("base.moduleMutation.invalid");
            }
        }

        private void Block(BaseModuleMutationBlock value)
        {
            Integer(value.Statements.Length); foreach (BaseModuleStatement statement in value.Statements) Statement(statement);
        }

        private void Statement(BaseModuleStatement value)
        {
            String(value.Id);
            switch (value)
            {
                case BaseModuleCreateStatement statement:
                    Integer(0); String(statement.CollectionId); Expression(statement.RecordId); Expression(statement.Payload); break;
                case BaseModulePatchStatement statement:
                    Integer(1); String(statement.CollectionId); Expression(statement.RecordId); Expression(statement.Patch); Optional(statement.ExpectedRevision); break;
                case BaseModuleReplaceStatement statement:
                    Integer(2); String(statement.CollectionId); Expression(statement.RecordId); Expression(statement.Payload); Optional(statement.ExpectedRevision); break;
                case BaseModuleDeleteStatement statement:
                    Integer(3); String(statement.CollectionId); Expression(statement.RecordId); Optional(statement.ExpectedRevision); break;
                case BaseModuleUpsertStatement statement:
                    Integer(4); String(statement.CollectionId); Expression(statement.RecordId); Expression(statement.Create); Expression(statement.Update);
                    Integer((int)statement.UpdateMode); Optional(statement.ExpectedRevision); break;
                case BaseModuleIncrementGenerationStatement statement:
                    Integer(5); String(statement.CaptureId); Boolean(statement.CreateIfAbsent); break;
                case BaseModuleIfStatement statement:
                    Integer(6); String(statement.GuardId); Block(statement.WhenTrue); Block(statement.WhenFalse); break;
                case BaseModuleRequireStatement statement:
                    Integer(7); String(statement.GuardId); String(statement.RequirementId); break;
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
                    Integer(0); Integer(expression.Property.StablePropertyPath.Length);
                    foreach (string edge in expression.Property.StablePropertyPath) String(edge);
                    String(expression.Property.DeclaredTypeId); break;
                case BaseModuleConstantExpression expression: Integer(1); Bytes(expression.CanonicalBaseJson.AsSpan()); break;
                case BaseModuleCapturedRecordIdExpression expression: Integer(2); String(expression.CaptureId); break;
                case BaseModuleCapturedRevisionExpression expression: Integer(3); String(expression.CaptureId); break;
                case BaseModuleCapturedFieldExpression expression: Integer(4); Field(expression.Field); break;
                case BaseModuleCapturedGenerationExpression expression: Integer(5); String(expression.CaptureId); break;
                case BaseModuleCommittedRecordIdExpression expression: Integer(6); String(expression.StatementId); break;
                case BaseModuleCommittedRevisionExpression expression: Integer(7); String(expression.StatementId); break;
                case BaseModuleCommittedUpsertDispositionExpression expression: Integer(8); String(expression.StatementId); break;
                case BaseModuleResultingGenerationExpression expression: Integer(9); String(expression.CaptureId); break;
                case BaseModuleCoalesceExpression expression:
                    Integer(10); Integer(expression.Values.Length); foreach (BaseModuleValueExpression child in expression.Values) Expression(child); break;
                case BaseModuleConditionalExpression expression:
                    Integer(11); String(expression.GuardId); Expression(expression.WhenTrue); Expression(expression.WhenFalse); break;
                case BaseModuleBinaryNumericExpression expression:
                    Integer(12); Integer((int)expression.Operator); Expression(expression.Left); Expression(expression.Right);
                    Boolean(expression.Decimal is not null);
                    if (expression.Decimal is { } context)
                    { Integer(context.Precision); Integer(context.Scale); Integer((int)context.Rounding); }
                    break;
                case BaseModuleObjectExpression expression:
                    Integer(13); Integer(expression.Properties.Length);
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
    }
}
