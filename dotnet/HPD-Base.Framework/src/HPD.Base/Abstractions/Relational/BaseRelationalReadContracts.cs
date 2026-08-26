using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Identifies the closed relational topology owned by a registered read.</summary>
public enum BaseRelationalReadTopology
{
    /// <summary>Uses one joined rowset.</summary>
    Ordinary = 0,
    /// <summary>Counts independent authorized sources in one snapshot.</summary>
    CompoundCount = 1,
}
/// <summary>Identifies a portable relational join operation.</summary>
public enum BaseJoinKind
{
    /// <summary>Identifies inner.</summary>
Inner,
    /// <summary>Identifies left.</summary>
Left,
    /// <summary>Identifies semi.</summary>
Semi,
    /// <summary>Identifies anti.</summary>
Anti
}

/// <summary>Identifies a portable relational aggregate.</summary>
public enum BaseAggregateKind
{
    /// <summary>Identifies count.</summary>
Count,
    /// <summary>Identifies count Distinct.</summary>
CountDistinct,
    /// <summary>Identifies sum.</summary>
Sum,
    /// <summary>Identifies average.</summary>
Average,
    /// <summary>Identifies minimum.</summary>
Minimum,
    /// <summary>Identifies maximum.</summary>
Maximum,
    /// <summary>Identifies any.</summary>
Any,
    /// <summary>Identifies all.</summary>
All
}

/// <summary>Identifies one closed relational operand branch.</summary>
public enum BaseRelationalOperandKind
{
    /// <summary>Identifies source Field.</summary>
SourceField,
    /// <summary>Identifies record Id.</summary>
    RecordId,
    /// <summary>Identifies the current authoritative record revision.</summary>
    RecordRevision,
    /// <summary>Identifies parameter.</summary>
Parameter,
    /// <summary>Identifies aggregate.</summary>
Aggregate,
    /// <summary>Identifies literal.</summary>
    Literal,
    /// <summary>Identifies the output-only current reference for one exported logical-subject contract.</summary>
    SubjectReference,
    /// <summary>Identifies an output-only projection of one already-stored exported-subject reference.</summary>
    StoredSubjectReference = 7
}

/// <summary>Defines consistency required by a relational read.</summary>
public enum BaseReadConsistency
{
    /// <summary>Identifies snapshot.</summary>
Snapshot
}

/// <summary>Defines dependency evidence required from read execution.</summary>
public enum BaseReadDependencyMode
{
    /// <summary>Identifies complete.</summary>
Complete
}

/// <summary>Defines one registered source in a relational read.</summary>
public sealed record BaseRelationalReadSource
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets collection Id.</summary>
    public required string CollectionId { get; init; }
}

/// <summary>Defines one closed typed relational operand.</summary>
public sealed record BaseRelationalOperand
{
    /// <summary>Gets or sets kind.</summary>
    public required BaseRelationalOperandKind Kind { get; init; }
    /// <summary>Gets or sets source Id.</summary>
    public string? SourceId { get; init; }
    /// <summary>Gets or sets field Id.</summary>
    public string? FieldId { get; init; }
    /// <summary>Gets or sets parameter Id.</summary>
    public string? ParameterId { get; init; }
    /// <summary>Gets or sets aggregate Id.</summary>
    public string? AggregateId { get; init; }
    /// <summary>Gets or sets literal.</summary>
    public QueryValue? Literal { get; init; }
    /// <summary>Gets the exported logical-subject contract identifier for an output-only subject reference.</summary>
    public string? SubjectContractId { get; init; }
    /// <summary>Gets the exported logical-subject contract version for an output-only subject reference.</summary>
    public int? SubjectContractVersion { get; init; }
}

/// <summary>Defines an equality join between two registered sources.</summary>
public sealed record BaseRelationalReadJoin
{
    /// <summary>Gets or sets kind.</summary>
    public required BaseJoinKind Kind { get; init; }
    /// <summary>Gets or sets left.</summary>
    public required BaseRelationalOperand Left { get; init; }
    /// <summary>Gets or sets right.</summary>
    public required BaseRelationalOperand Right { get; init; }
}

/// <summary>Defines one closed relational predicate node.</summary>
public sealed record BaseRelationalPredicate
{
    /// <summary>Gets or sets kind.</summary>
    public required FilterNodeKind Kind { get; init; }
    /// <summary>Gets or sets operator.</summary>
    public FilterOperator Operator { get; init; }
    /// <summary>Gets or sets left.</summary>
    public BaseRelationalOperand? Left { get; init; }
    /// <summary>Gets or sets right.</summary>
    public BaseRelationalOperand? Right { get; init; }
    /// <summary>Gets or sets children.</summary>
    public BaseRelationalPredicate[]? Children { get; init; }
}

/// <summary>Defines one registered aggregate output.</summary>
public sealed record BaseRelationalReadAggregate
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets kind.</summary>
    public required BaseAggregateKind Kind { get; init; }
    /// <summary>Gets or sets operand.</summary>
    public BaseRelationalOperand? Operand { get; init; }
}

/// <summary>Maps one projection field to a closed operand.</summary>
public sealed record BaseRelationalReadProjection
{
    /// <summary>Gets or sets field Id.</summary>
    public required string FieldId { get; init; }
    /// <summary>Gets or sets operand.</summary>
    public required BaseRelationalOperand Operand { get; init; }
    /// <summary>Gets the exact installed source-field authority for a canonical-JSON projection.</summary>
    public BaseReadCanonicalJsonAuthority? CanonicalJsonAuthority { get; init; }
}

/// <summary>Defines the immutable installed source-field authority for one canonical-JSON read value.</summary>
public sealed record BaseReadCanonicalJsonAuthority
{
    /// <summary>Gets the owning collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the source field identifier.</summary>
    public required string FieldId { get; init; }
    /// <summary>Gets the installed scalar-constraint checksum.</summary>
    public required BaseScalarConstraintChecksum ConstraintChecksum { get; init; }
    /// <summary>Gets the maximum canonical UTF-8 byte count.</summary>
    public required int MaximumCanonicalJsonBytes { get; init; }
    /// <summary>Gets the admitted top-level JSON shape.</summary>
    public required BaseJsonShape JsonShape { get; init; }
    /// <summary>Gets the maximum nesting depth.</summary>
    public required int MaximumJsonDepth { get; init; }
    /// <summary>Gets the maximum items in each array.</summary>
    public required int MaximumJsonArrayItems { get; init; }
    /// <summary>Gets the maximum properties in each object.</summary>
    public required int MaximumJsonObjectProperties { get; init; }
    /// <summary>Gets the maximum total node count.</summary>
    public required int MaximumJsonTotalNodes { get; init; }
    /// <summary>Gets the maximum aggregate UTF-8 bytes in string values.</summary>
    public required int MaximumJsonTotalStringUtf8Bytes { get; init; }
    /// <summary>Gets the maximum aggregate UTF-8 bytes in property names.</summary>
    public required int MaximumJsonTotalNameUtf8Bytes { get; init; }
    /// <summary>Gets the purpose-bound complete authority checksum.</summary>
    public required BaseSchemaAuthorityChecksum AuthorityChecksum { get; init; }
}

/// <summary>Defines one deterministic relational sort.</summary>
public sealed record BaseRelationalReadSort
{
    /// <summary>Gets or sets operand.</summary>
    public required BaseRelationalOperand Operand { get; init; }
    /// <summary>Gets or sets direction.</summary>
    public QuerySortDirection Direction { get; init; }
    /// <summary>Gets or sets nulls.</summary>
    public QueryNullOrder Nulls { get; init; }
}

/// <summary>Defines immutable execution budgets for one registered read.</summary>
public sealed record BaseRelationalReadBudgets
{
    /// <summary>Gets or sets max Result Rows.</summary>
    public required int MaxResultRows { get; init; }
    /// <summary>Gets or sets max Result Bytes.</summary>
    public required int MaxResultBytes { get; init; }
    /// <summary>Gets or sets max Operations.</summary>
    public required int MaxOperations { get; init; }
    /// <summary>Gets the exact maximum provider execution time in milliseconds.</summary>
    public required int MaxExecutionMilliseconds { get; init; }
    /// <summary>Gets the maximum installed independent count branches.</summary>
    public required int MaxCompoundBranches { get; init; }
    /// <summary>Gets the maximum aggregate operations across independent count branches.</summary>
    public required int MaxCompoundOperations { get; init; }
}

/// <summary>Defines one independent count branch in a compound registered read.</summary>
public sealed record BaseRelationalCompoundCountBranch
{
    /// <summary>Gets the stable branch identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the branch-owned source.</summary>
    public required BaseRelationalReadSource Source { get; init; }
    /// <summary>Gets the optional closed branch predicate.</summary>
    public BaseRelationalPredicate? Predicate { get; init; }
    /// <summary>Gets the installed public discriminator.</summary>
    public required string Discriminator { get; init; }
    /// <summary>Gets the discriminator output field identifier.</summary>
    public required string DiscriminatorOutputFieldId { get; init; }
    /// <summary>Gets the count output field identifier.</summary>
    public required string CountOutputFieldId { get; init; }
    /// <summary>Gets the purpose-bound branch authority checksum.</summary>
    public required BaseSchemaAuthorityChecksum BranchChecksum { get; init; }
}

/// <summary>Proves one compound branch was evaluated under installed provider authority.</summary>
public sealed record BaseRelationalCompoundBranchEvidence
{
    /// <summary>Gets the installed branch identifier.</summary>
    public required string BranchId { get; init; }
    /// <summary>Gets the installed branch checksum.</summary>
    public required BaseSchemaAuthorityChecksum BranchChecksum { get; init; }
    /// <summary>Gets the exact zero-based output ordinal.</summary>
    public required int RowOrdinal { get; init; }
    /// <summary>Gets the provider schema generation used for evaluation.</summary>
    public required long SchemaGeneration { get; init; }
}

/// <summary>Defines one closed typed parameter accepted by a registered read.</summary>
public sealed record BaseRelationalReadParameter
{
    /// <summary>Gets the stable parameter identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the canonical scalar kind, or <see cref = "QueryValueKind.Array"/> for an array parameter.</summary>
    public required QueryValueKind Kind { get; init; }
    /// <summary>Gets the canonical element kind for an array parameter.</summary>
    public QueryValueKind? ElementKind { get; init; }
    /// <summary>Gets whether the complete parameter may be null.</summary>
    public bool Nullable { get; init; }
    /// <summary>Gets the maximum string or identifier length, when applicable.</summary>
    public int? MaxLength { get; init; }
    /// <summary>Gets the maximum number of array elements, when applicable.</summary>
    public int? MaxItems { get; init; }
    /// <summary>Gets the exact installed source-field authority for a canonical-JSON parameter.</summary>
    public BaseReadCanonicalJsonAuthority? CanonicalJsonAuthority { get; init; }
}

/// <summary>Defines the complete closed provider-neutral relational read plan.</summary>
public sealed record BaseRelationalReadPlan
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the closed registered-read topology.</summary>
    public required BaseRelationalReadTopology Topology { get; init; }
    /// <summary>Gets the immutable independent count branches.</summary>
    public BaseRelationalCompoundCountBranch[] CompoundCountBranches { get; init; } = [];
    /// <summary>Gets the purpose-bound compound-plan checksum.</summary>
    public BaseSchemaAuthorityChecksum? CompoundChecksum { get; init; }
    /// <summary>Gets or sets schema Generation.</summary>
    public long SchemaGeneration { get; init; }
    /// <summary>Gets or sets sources.</summary>
    public required BaseRelationalReadSource[] Sources { get; init; }
    /// <summary>Gets or sets joins.</summary>
    public BaseRelationalReadJoin[] Joins { get; init; } = [];
    /// <summary>Gets or sets predicate.</summary>
    public BaseRelationalPredicate? Predicate { get; init; }
    /// <summary>Gets or sets group Keys.</summary>
    public BaseRelationalOperand[] GroupKeys { get; init; } = [];
    /// <summary>Gets or sets aggregates.</summary>
    public BaseRelationalReadAggregate[] Aggregates { get; init; } = [];
    /// <summary>Gets or sets having.</summary>
    public BaseRelationalPredicate? Having { get; init; }
    /// <summary>Gets or sets projection.</summary>
    public required BaseRelationalReadProjection[] Projection { get; init; }
    /// <summary>Gets or sets distinct.</summary>
    public bool Distinct { get; init; }
    /// <summary>Gets or sets sort.</summary>
    public BaseRelationalReadSort[] Sort { get; init; } = [];
    /// <summary>Gets or sets parameters.</summary>
    public required BaseRelationalReadParameter[] Parameters { get; init; }
    /// <summary>Gets or sets consistency.</summary>
    public BaseReadConsistency Consistency { get; init; } = BaseReadConsistency.Snapshot;
    /// <summary>Gets or sets dependency Mode.</summary>
    public BaseReadDependencyMode DependencyMode { get; init; } = BaseReadDependencyMode.Complete;
    /// <summary>Gets or sets budgets.</summary>
    public required BaseRelationalReadBudgets Budgets { get; init; }
    /// <summary>Gets the installed pagination authority.</summary>
    public required BaseRegisteredReadPaginationAuthority Pagination { get; init; }
    /// <summary>Gets the Runtime-owned execution window; installed definitions require null.</summary>
    public BaseRegisteredReadWindow? Window { get; init; }
}

/// <summary>Identifies the installed pagination modes of one registered read.</summary>
public enum BaseRegisteredReadPaginationMode
{
    /// <summary>Only one-based page-number execution is authorized.</summary>
    PageOnly = 0,
    /// <summary>Page-number and explicitly bounded arbitrary-offset execution are authorized.</summary>
    PageAndOffset = 1,
}

/// <summary>Defines immutable installed registered-read pagination authority.</summary>
public sealed record BaseRegisteredReadPaginationAuthority
{
    /// <summary>Gets the admitted pagination mode.</summary>
    public required BaseRegisteredReadPaginationMode Mode { get; init; }
    /// <summary>Gets the maximum admitted offset; page-only authority requires zero.</summary>
    public required int MaximumOffset { get; init; }
}

/// <summary>Identifies one Runtime-owned registered-read execution window.</summary>
public enum BaseRegisteredReadWindowKind
{
    /// <summary>Executes a one-based page-number window.</summary>
    Page = 0,
    /// <summary>Executes a zero-based arbitrary-offset window.</summary>
    Offset = 1,
}

/// <summary>Defines one closed Runtime-owned registered-read execution window.</summary>
public sealed record BaseRegisteredReadWindow
{
    /// <summary>Gets the window kind.</summary>
    public required BaseRegisteredReadWindowKind Kind { get; init; }
    /// <summary>Gets the one-based page number for page mode.</summary>
    public int? Page { get; init; }
    /// <summary>Gets the page size for page mode.</summary>
    public int? PerPage { get; init; }
    /// <summary>Gets the zero-based offset for offset mode.</summary>
    public int? Offset { get; init; }
    /// <summary>Gets the result limit for offset mode.</summary>
    public int? Limit { get; init; }
}

/// <summary>Defines one canonical relational row value.</summary>
public sealed record BaseRelationalRow
{
    /// <summary>Gets or sets fields.</summary>
    public required BaseRelationalFieldValue[] Fields { get; init; }
}

/// <summary>Associates one stable projection field with its closed value.</summary>
public sealed record BaseRelationalFieldValue
{
    /// <summary>Gets or sets field Id.</summary>
    public required string FieldId { get; init; }
    /// <summary>Gets or sets value.</summary>
    public required QueryValue Value { get; init; }
}

/// <summary>Returns a completely buffered and validated provider result.</summary>
public sealed record BaseRelationalReadResult
{
    /// <summary>Gets or sets rows.</summary>
    public required BaseRelationalRow[] Rows { get; init; }
    /// <summary>Gets or sets page.</summary>
    public required PageInfo Page { get; init; }
    /// <summary>Gets or sets count.</summary>
    public long? Count { get; init; }
    /// <summary>Gets or sets schema Generation.</summary>
    public long SchemaGeneration { get; init; }
}

/// <summary>Binds one closed request value to a stable parameter identifier.</summary>
public sealed record BaseRelationalParameterValue
{
    /// <summary>Gets or sets parameter Id.</summary>
    public required string ParameterId { get; init; }
    /// <summary>Gets or sets value.</summary>
    public required QueryValue Value { get; init; }
}

/// <summary>Provides one source's current independently evaluated read policy.</summary>
public sealed record BaseRelationalReadSourcePolicy
{
    /// <summary>Gets the definition-local source identifier.</summary>
    public required string SourceId { get; init; }
    /// <summary>Gets the stable source collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the current record-membership constraint.</summary>
    public FilterExpression? Filter { get; init; }
    /// <summary>Gets the current field-visibility constraint.</summary>
    public FieldMask? ReadMask { get; init; }
}

/// <summary>Defines one bounded provider execution request.</summary>
public sealed record BaseRelationalReadExecutionRequest
{
    /// <summary>Gets or sets plan.</summary>
    public required BaseRelationalReadPlan Plan { get; init; }
    /// <summary>Gets or sets parameter Values.</summary>
    public required BaseRelationalParameterValue[] ParameterValues { get; init; }
    /// <summary>Gets or sets source Policies.</summary>
    public required BaseRelationalReadSourcePolicy[] SourcePolicies { get; init; }
    /// <summary>Gets or sets operation.</summary>
    public required OperationContext Operation { get; init; }
    /// <summary>Gets or sets acquisition Timeout.</summary>
    public TimeSpan AcquisitionTimeout { get; init; }
    /// <summary>Gets or sets execution Timeout.</summary>
    public TimeSpan ExecutionTimeout { get; init; }
    /// <summary>Gets or sets max Result Rows.</summary>
    public int MaxResultRows { get; init; }
    /// <summary>Gets or sets max Result Bytes.</summary>
    public int MaxResultBytes { get; init; }
}

/// <summary>Returns complete rows and dependency evidence from a provider.</summary>
public sealed record BaseRelationalReadExecutionResult
{
    /// <summary>Gets or sets result.</summary>
    public required BaseRelationalReadResult Result { get; init; }
    /// <summary>Gets or sets dependency Evidence.</summary>
    public required BaseReadDependencyEvidence[] DependencyEvidence { get; init; }
    /// <summary>Gets exact evidence for every installed compound branch.</summary>
    public BaseRelationalCompoundBranchEvidence[] CompoundBranches { get; init; } = [];
}

/// <summary>Describes trusted same-snapshot evidence before Runtime protection.</summary>
public sealed record BaseReadDependencyEvidence
{
    /// <summary>Gets the contributing collection identity.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets an optional contributing record identity.</summary>
    public string? RecordId { get; init; }
    /// <summary>Gets the protected exported-subject contract identity, when this evidence came from an acquisition projection.</summary>
    public string? SubjectContractId { get; init; }
    /// <summary>Gets the positive exported-subject contract version.</summary>
    public int? SubjectContractVersion { get; init; }
    /// <summary>Gets the positive current exported-subject publication generation.</summary>
    public long? SubjectStateGeneration { get; init; }
}

/// <summary>Describes callable relational-read provider support.</summary>
public sealed record RelationalReadCapability
{
    /// <summary>Gets or sets supported.</summary>
    public bool Supported { get; init; }
    /// <summary>Gets or sets join Kinds.</summary>
    public required BaseJoinKind[] JoinKinds { get; init; }
    /// <summary>Gets or sets aggregate Kinds.</summary>
    public required BaseAggregateKind[] AggregateKinds { get; init; }
    /// <summary>Gets or sets comparison Operators.</summary>
    public required FilterOperator[] ComparisonOperators { get; init; }
    /// <summary>Gets or sets value Kinds.</summary>
    public required QueryValueKind[] ValueKinds { get; init; }
    /// <summary>Gets whether source-bound canonical-JSON values are supported.</summary>
    public bool CanonicalJsonValues { get; init; }
    /// <summary>Gets whether independent aggregate branches are supported.</summary>
    public bool IndependentAggregateBranches { get; init; }
    /// <summary>Gets whether all compound branches execute under one snapshot.</summary>
    public bool SingleSnapshotCompoundReads { get; init; }
    /// <summary>Gets the maximum supported compound branches.</summary>
    public int MaxCompoundBranches { get; init; }
    /// <summary>Gets the maximum supported compound operations.</summary>
    public int MaxCompoundOperations { get; init; }
    /// <summary>Gets or sets max Sources.</summary>
    public int MaxSources { get; init; }
    /// <summary>Gets or sets max Joins.</summary>
    public int MaxJoins { get; init; }
    /// <summary>Gets or sets max Predicate Nodes.</summary>
    public int MaxPredicateNodes { get; init; }
    /// <summary>Gets or sets max Group Keys.</summary>
    public int MaxGroupKeys { get; init; }
    /// <summary>Gets or sets max Aggregates.</summary>
    public int MaxAggregates { get; init; }
    /// <summary>Gets or sets max Projection Fields.</summary>
    public int MaxProjectionFields { get; init; }
    /// <summary>Gets or sets max Sort Fields.</summary>
    public int MaxSortFields { get; init; }
    /// <summary>Gets or sets max Result Rows.</summary>
    public int MaxResultRows { get; init; }
    /// <summary>Gets or sets max Result Bytes.</summary>
    public int MaxResultBytes { get; init; }
    /// <summary>Gets or sets snapshot Consistency.</summary>
    public bool SnapshotConsistency { get; init; }
    /// <summary>Gets or sets complete Dependency Evidence.</summary>
    public bool CompleteDependencyEvidence { get; init; }
}

/// <summary>Owns the canonical immutable authority of a relational-read capability.</summary>
public static class BaseRelationalReadCapabilityContract
{
    /// <summary>Returns the closed unsupported capability.</summary>
    public static RelationalReadCapability Unsupported() => new()
    {
        JoinKinds = [], AggregateKinds = [], ComparisonOperators = [], ValueKinds = [],
    };

    /// <summary>Creates a defensive immutable copy.</summary>
    /// <param name="value">The capability to copy.</param>
    public static RelationalReadCapability Clone(RelationalReadCapability value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value with
        {
            JoinKinds = value.JoinKinds.ToArray(),
            AggregateKinds = value.AggregateKinds.ToArray(),
            ComparisonOperators = value.ComparisonOperators.ToArray(),
            ValueKinds = value.ValueKinds.ToArray(),
        };
    }

    /// <summary>Returns whether the complete capability has one closed, bounded shape.</summary>
    /// <param name="value">The capability to validate.</param>
    public static bool IsValid(RelationalReadCapability? value) => value is not null
        && value.JoinKinds is not null && value.AggregateKinds is not null
        && value.ComparisonOperators is not null && value.ValueKinds is not null
        && value.JoinKinds.All(Enum.IsDefined) && value.AggregateKinds.All(Enum.IsDefined)
        && value.ComparisonOperators.All(Enum.IsDefined) && value.ValueKinds.All(Enum.IsDefined)
        && value.JoinKinds.Distinct().Count() == value.JoinKinds.Length
        && value.AggregateKinds.Distinct().Count() == value.AggregateKinds.Length
        && value.ComparisonOperators.Distinct().Count() == value.ComparisonOperators.Length
        && value.ValueKinds.Distinct().Count() == value.ValueKinds.Length
        && value.MaxSources >= 0 && value.MaxJoins >= 0 && value.MaxPredicateNodes >= 0
        && value.MaxGroupKeys >= 0 && value.MaxAggregates >= 0 && value.MaxProjectionFields >= 0
        && value.MaxSortFields >= 0 && value.MaxResultRows >= 0 && value.MaxResultBytes >= 0
        && value.MaxCompoundBranches >= 0 && value.MaxCompoundOperations >= 0
        && (value.IndependentAggregateBranches || value.SingleSnapshotCompoundReads
            ? value.Supported && value.MaxCompoundBranches > 0 && value.MaxCompoundOperations > 0
            : value.MaxCompoundBranches == 0 && value.MaxCompoundOperations == 0);

    /// <summary>Computes the canonical SHA-256 capability checksum.</summary>
    /// <param name="value">The capability authority.</param>
    public static ImmutableArray<byte> Checksum(RelationalReadCapability value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new ArrayBufferWriter<byte>();
        Raw(writer, "hpd.base.relational-read-capability.v1\0"u8);
        Boolean(writer, value.Supported);
        Enums(writer, value.JoinKinds); Enums(writer, value.AggregateKinds);
        Enums(writer, value.ComparisonOperators); Enums(writer, value.ValueKinds);
        Boolean(writer, value.CanonicalJsonValues);
        Integer(writer, value.MaxSources); Integer(writer, value.MaxJoins);
        Integer(writer, value.MaxPredicateNodes); Integer(writer, value.MaxGroupKeys);
        Integer(writer, value.MaxAggregates);
        Boolean(writer, value.IndependentAggregateBranches);
        Boolean(writer, value.SingleSnapshotCompoundReads);
        Integer(writer, value.MaxCompoundBranches); Integer(writer, value.MaxCompoundOperations);
        Integer(writer, value.MaxProjectionFields); Integer(writer, value.MaxSortFields);
        Integer(writer, value.MaxResultRows); Integer(writer, value.MaxResultBytes);
        Boolean(writer, value.SnapshotConsistency); Boolean(writer, value.CompleteDependencyEvidence);
        return SHA256.HashData(writer.WrittenSpan).ToImmutableArray();
    }

    private static void Enums<T>(IBufferWriter<byte> writer, IEnumerable<T> values) where T : struct, Enum
    {
        int[] ordered = values.Select(static value => Convert.ToInt32(value)).Order().ToArray();
        Integer(writer, ordered.Length); foreach (int value in ordered) Integer(writer, value);
    }
    private static void Boolean(IBufferWriter<byte> writer, bool value) => Integer(writer, value ? 1 : 0);
    private static void Integer(IBufferWriter<byte> writer, int value)
    { Span<byte> bytes = writer.GetSpan(4); BinaryPrimitives.WriteInt32BigEndian(bytes, value); writer.Advance(4); }
    private static void Raw(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    { value.CopyTo(writer.GetSpan(value.Length)); writer.Advance(value.Length); }
}

internal static class BaseRelationalReadEvidenceAccounting
{
    internal static bool TryMeasure(
        BaseReadDependencyEvidence[] dependencies,
        BaseRelationalCompoundBranchEvidence[] branches,
        out long bytes)
    {
        try
        {
            long total = 0;
            foreach (BaseReadDependencyEvidence dependency in dependencies)
            {
                total = checked(total + Text(dependency.CollectionId) + Text(dependency.RecordId)
                    + Text(dependency.SubjectContractId) + (dependency.SubjectContractVersion.HasValue ? 4 : 0)
                    + (dependency.SubjectStateGeneration.HasValue ? 8 : 0));
            }
            foreach (BaseRelationalCompoundBranchEvidence branch in branches)
                total = checked(total + Text(branch.BranchId) + branch.BranchChecksum.ToArray().Length + 4 + 8);
            bytes = total;
            return true;
        }
        catch (OverflowException)
        {
            bytes = 0;
            return false;
        }
    }

    private static int Text(string? value) => value is null ? 0 : checked(4 + Encoding.UTF8.GetByteCount(value));
}

/// <summary>Executes complete registered relational reads.</summary>
public interface IRelationalReadStore : IRecordStore
{
    /// <summary>Gets relational Reads.</summary>
    RelationalReadCapability RelationalReads { get; }

    /// <summary>Performs execute Read Async.</summary>
    ValueTask<OperationResult<BaseRelationalReadExecutionResult>> ExecuteReadAsync(BaseRelationalReadExecutionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Describes callable snapshot-consistent include support.</summary>
public sealed record RecordIncludeExecutionCapability
{
    /// <summary>Gets or sets supported.</summary>
    public bool Supported { get; init; }
    /// <summary>Gets or sets max Depth.</summary>
    public int MaxDepth { get; init; }
    /// <summary>Gets or sets max Includes.</summary>
    public int MaxIncludes { get; init; }
    /// <summary>Gets or sets max Records.</summary>
    public int MaxRecords { get; init; }
    /// <summary>Gets or sets snapshot Consistency.</summary>
    public bool SnapshotConsistency { get; init; }
}

/// <summary>Provides the already-composed policy for one include source.</summary>
public sealed record RecordIncludeSourcePolicy
{
    /// <summary>Gets or sets collection Id.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets filter.</summary>
    public FilterExpression? Filter { get; init; }
    /// <summary>Gets or sets read Mask.</summary>
    public FieldMask? ReadMask { get; init; }
    /// <summary>Gets the exact schema-visible field identifiers for this source.</summary>
    public required string[] VisibleFieldIds { get; init; }
    /// <summary>Gets whether policy denied reading every record from this source.</summary>
    public bool Denied { get; init; }
}

/// <summary>Defines one bounded snapshot-consistent include request.</summary>
public sealed record RecordIncludeExecutionRequest
{
    /// <summary>Gets or sets root Collection.</summary>
    public required CollectionDefinition RootCollection { get; init; }
    /// <summary>Gets or sets root Query.</summary>
    public required RecordQuery RootQuery { get; init; }
    /// <summary>Gets or sets include Plan.</summary>
    public required RecordInclude[] IncludePlan { get; init; }
    /// <summary>Gets or sets source Policies.</summary>
    public required RecordIncludeSourcePolicy[] SourcePolicies { get; init; }
    /// <summary>Gets or sets operation.</summary>
    public required OperationContext Operation { get; init; }
    /// <summary>Gets or sets acquisition Timeout.</summary>
    public TimeSpan AcquisitionTimeout { get; init; }
    /// <summary>Gets or sets execution Timeout.</summary>
    public TimeSpan ExecutionTimeout { get; init; }
    /// <summary>Gets or sets max Result Rows.</summary>
    public int MaxResultRows { get; init; }
    /// <summary>Gets or sets max Result Bytes.</summary>
    public int MaxResultBytes { get; init; }
}

/// <summary>Returns one complete root page with structural includes.</summary>
public sealed record RecordIncludeExecutionResult
{
    /// <summary>Gets or sets page.</summary>
    public required RecordPage Page { get; init; }
    /// <summary>Gets or sets schema Generation.</summary>
    public long SchemaGeneration { get; init; }
    /// <summary>Gets or sets dependency Evidence.</summary>
    public required BaseReadDependencyEvidence[] DependencyEvidence { get; init; }
}

/// <summary>Executes structural includes under one provider snapshot.</summary>
public interface IConsistentRecordIncludeStore : IRecordStore
{
    /// <summary>Gets includes.</summary>
    RecordIncludeExecutionCapability Includes { get; }

    /// <summary>Performs execute Include Async.</summary>
    ValueTask<OperationResult<RecordIncludeExecutionResult>> ExecuteIncludeAsync(RecordIncludeExecutionRequest request, CancellationToken cancellationToken = default);
}
