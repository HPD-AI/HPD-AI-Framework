using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Names fixed policy-safe lexical-search grants.</summary>
public static class BaseTextGrants
{
    /// <summary>Executes one installed lexical index.</summary>
    public const string Query = "base.text.query";
    /// <summary>Reads bounded installed index metadata.</summary>
    public const string IndexRead = "base.text.index.read";
    /// <summary>Reads bounded provider diagnostics for one index.</summary>
    public const string DiagnosticsRead = "base.text.diagnostics.read";
    /// <summary>Executes one identified generation-guarded rebuild.</summary>
    public const string Rebuild = "base.text.rebuild";
}

/// <summary>Identifies the one portable lexical analyzer owned by BASE.</summary>
public enum BaseTextAnalyzerKind
{
    /// <summary>Uses the pinned Unicode compatibility-normalization and full case-folding contract.</summary>
    UnicodeCaseFolded = 0,
}

/// <summary>Lists stable identities and receipts for the portable text analyzer.</summary>
public static class BaseTextAnalyzers
{
    /// <summary>The stable v1 analyzer contract identity.</summary>
    public const string UnicodeCaseFoldedV1 = "hpd.base.text.analyzer.unicode-case-folded.v1";
    /// <summary>The pinned Unicode release.</summary>
    public const string UnicodeVersion = "17.0.0";
    /// <summary>The normative Unicode source receipt SHA-256.</summary>
    public const string UnicodeSourceReceiptSha256 = "957d9ea3b8d9c05ee415d17c1d1bd522d0a474448ce9c5dc39d3e3345cbd6ed2";
    /// <summary>The maximum normalized UTF-8 bytes in one token.</summary>
    public const int MaximumTokenBytes = 64;
    /// <summary>The maximum tokens contributed by one field.</summary>
    public const int MaximumTokensPerField = 4096;
    /// <summary>The maximum normalized UTF-8 bytes contributed by one field.</summary>
    public const int MaximumNormalizedBytesPerField = 256 * 1024;
}

/// <summary>Provides defensive copies of the normative analyzer and scoring receipts.</summary>
public static class BaseTextContractReceipts
{
    private static readonly byte[] Analyzer = CreateAnalyzer();
    private static readonly byte[] Scoring = CreateScoring();
    /// <summary>Gets the exact analyzer receipt bytes.</summary>
    public static ImmutableArray<byte> AnalyzerReceipt => ImmutableArray.Create(Analyzer.ToArray());
    /// <summary>Gets the exact scoring receipt digest.</summary>
    public static ImmutableArray<byte> ScoringReceipt => ImmutableArray.Create(Scoring.ToArray());
    private static byte[] CreateAnalyzer()
    {
        byte[] id = System.Text.Encoding.ASCII.GetBytes(BaseTextAnalyzers.UnicodeCaseFoldedV1);
        byte[] version = System.Text.Encoding.ASCII.GetBytes(BaseTextAnalyzers.UnicodeVersion);
        byte[] checksum = Convert.FromHexString(BaseTextAnalyzers.UnicodeSourceReceiptSha256);
        return [.. id, 0, .. version, 0, .. checksum];
    }
    private static byte[] CreateScoring()
    {
        using var stream = new MemoryStream();
        stream.Write("HPDB-TEXT-SCORING-RECEIPT-1\0"u8);
        static void U64(Stream target, ulong value) { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes, value); target.Write(bytes); }
        byte[] id = System.Text.Encoding.UTF8.GetBytes(BaseTextScoring.ContractId);
        Span<byte> count = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(count, checked((uint)id.Length)); stream.Write(count); stream.Write(id);
        U64(stream, BaseTextScoring.Scale); U64(stream, 6); U64(stream, 5); U64(stream, 3); U64(stream, 4); U64(stream, 1); U64(stream, 0);
        return System.Security.Cryptography.SHA256.HashData(stream.ToArray());
    }
}

/// <summary>Contains one exact provider-neutral fixed-point lexical relevance score.</summary>
public readonly record struct BaseTextScore : IComparable<BaseTextScore>
{
    /// <summary>Gets the exact score units; larger values rank first.</summary>
    public required ulong Units { get; init; }
    /// <inheritdoc />
    public int CompareTo(BaseTextScore other) => Units.CompareTo(other.Units);
}

/// <summary>Declares one ordinary secondary relevance tie-breaker.</summary>
public readonly record struct BaseTextOrder(string StableFieldId, QuerySortDirection Direction, QueryNullOrder NullOrder);

/// <summary>Identifies a value usable by a pre-ranking text-index constraint.</summary>
public enum BaseTextFilterValueKind
{
    /// <summary>A UTF-8 string value.</summary>
    String = 0,
    /// <summary>A Boolean value.</summary>
    Boolean = 1,
    /// <summary>A signed 64-bit integer value.</summary>
    Integer = 2,
    /// <summary>A stable identifier value.</summary>
    Id = 3,
}

/// <summary>Contains exact safety bounds for one text index and query.</summary>
public sealed record BaseTextExecutionLimits
{
    /// <summary>Gets the maximum lexical query nodes.</summary>
    public required int MaximumQueryNodes { get; init; }
    /// <summary>Gets the maximum lexical query depth.</summary>
    public required int MaximumQueryDepth { get; init; }
    /// <summary>Gets the maximum terms in one phrase.</summary>
    public required int MaximumPhraseTerms { get; init; }
    /// <summary>Gets the maximum canonical query bytes.</summary>
    public required long MaximumQueryBytes { get; init; }
    /// <summary>Gets the maximum ordinary-filter nodes.</summary>
    public required int MaximumFilterNodes { get; init; }
    /// <summary>Gets the maximum ordinary-filter depth.</summary>
    public required int MaximumFilterDepth { get; init; }
    /// <summary>Gets the maximum ordinary-filter literals.</summary>
    public required int MaximumFilterLiterals { get; init; }
    /// <summary>Gets the maximum values in one IN node.</summary>
    public required int MaximumInValues { get; init; }
    /// <summary>Gets the maximum distinct expansions for one prefix.</summary>
    public required int MaximumPrefixExpansions { get; init; }
    /// <summary>Gets the maximum aggregate UTF-8 prefix-expansion bytes.</summary>
    public required long MaximumPrefixExpansionBytes { get; init; }
    /// <summary>Gets the maximum secondary ordering fields.</summary>
    public required int MaximumSecondaryOrderFields { get; init; }
    /// <summary>Gets the maximum canonical ordering bytes.</summary>
    public required long MaximumOrderingBytes { get; init; }
    /// <summary>Gets the maximum provider candidates, including the continuation probe.</summary>
    public required int MaximumCandidates { get; init; }
    /// <summary>Gets the maximum score-proof bytes.</summary>
    public required long MaximumScoreProofBytes { get; init; }
    /// <summary>Gets the maximum indexed tokens per field.</summary>
    public required int MaximumTokensPerField { get; init; }
    /// <summary>Gets the maximum normalized bytes per field.</summary>
    public required long MaximumNormalizedBytesPerField { get; init; }
    /// <summary>Gets the maximum normalized bytes per record.</summary>
    public required long MaximumNormalizedBytesPerRecord { get; init; }
    /// <summary>Gets the maximum returned records.</summary>
    public required int MaximumResults { get; init; }
    /// <summary>Gets the maximum returned bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the maximum protected cursor bytes.</summary>
    public required int MaximumCursorBytes { get; init; }
    /// <summary>Gets the maximum provider statement parameters.</summary>
    public required int MaximumStatementParameters { get; init; }
    /// <summary>Gets the maximum retained transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the query deadline.</summary>
    public required TimeSpan QueryTimeout { get; init; }
    /// <summary>Gets the consistency-wait deadline.</summary>
    public required TimeSpan ConsistencyWaitTimeout { get; init; }
}

/// <summary>Provides the closed portable text-search platform profile.</summary>
public static class BaseTextPlatform
{
    /// <summary>Gets a fresh immutable copy of the default v1 limits.</summary>
    public static BaseTextExecutionLimits DefaultLimits => new()
    {
        MaximumQueryNodes = 64,
        MaximumQueryDepth = 12,
        MaximumPhraseTerms = 16,
        MaximumQueryBytes = 32 * 1024,
        MaximumFilterNodes = 64,
        MaximumFilterDepth = 12,
        MaximumFilterLiterals = 256,
        MaximumInValues = 64,
        MaximumPrefixExpansions = 256,
        MaximumPrefixExpansionBytes = 16 * 1024,
        MaximumSecondaryOrderFields = 4,
        MaximumOrderingBytes = 8 * 1024,
        MaximumCandidates = 257,
        MaximumScoreProofBytes = 1024 * 1024,
        MaximumTokensPerField = BaseTextAnalyzers.MaximumTokensPerField,
        MaximumNormalizedBytesPerField = BaseTextAnalyzers.MaximumNormalizedBytesPerField,
        MaximumNormalizedBytesPerRecord = 1024 * 1024,
        MaximumResults = 256,
        MaximumResultBytes = 1024 * 1024,
        MaximumCursorBytes = 2 * 1024,
        MaximumStatementParameters = 1024,
        MaximumTransientBytes = 32_000_000,
        QueryTimeout = TimeSpan.FromSeconds(30),
        ConsistencyWaitTimeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>Creates the complete built-in provider capability for the specified authority class.</summary>
    public static BaseTextProviderCapability ProviderCapability(BaseTextProviderClass providerClass) => new()
    {
        ProviderClass = providerClass,
        TransactionalMaintenanceSupported = providerClass == BaseTextProviderClass.CoLocatedTransactional,
        ExactRevisionHydrationSupported = true,
        PolicyBeforeRankingSupported = true,
        ExactFixedPointScoreSupported = true,
        MaximumIndexesPerCollection = 8,
        MaximumFieldsPerIndex = 8,
        MaximumFilterFields = 16,
        MaximumQueryNodes = DefaultLimits.MaximumQueryNodes,
        MaximumQueryDepth = DefaultLimits.MaximumQueryDepth,
        MaximumPhraseTerms = DefaultLimits.MaximumPhraseTerms,
        MaximumQueryBytes = DefaultLimits.MaximumQueryBytes,
        MaximumFilterNodes = DefaultLimits.MaximumFilterNodes,
        MaximumFilterDepth = DefaultLimits.MaximumFilterDepth,
        MaximumFilterLiterals = DefaultLimits.MaximumFilterLiterals,
        MaximumInValues = DefaultLimits.MaximumInValues,
        MaximumPrefixExpansions = DefaultLimits.MaximumPrefixExpansions,
        MaximumPrefixExpansionBytes = DefaultLimits.MaximumPrefixExpansionBytes,
        MaximumSecondaryOrderFields = DefaultLimits.MaximumSecondaryOrderFields,
        MaximumOrderingBytes = DefaultLimits.MaximumOrderingBytes,
        MaximumCandidates = DefaultLimits.MaximumCandidates,
        MaximumScoreProofBytes = DefaultLimits.MaximumScoreProofBytes,
        MaximumTokensPerRecord = 8 * BaseTextAnalyzers.MaximumTokensPerField,
        MaximumNormalizedBytesPerField = DefaultLimits.MaximumNormalizedBytesPerField,
        MaximumNormalizedBytesPerRecord = DefaultLimits.MaximumNormalizedBytesPerRecord,
        MaximumIndexedRecords = 1_000_000,
        MaximumPostings = 50_000_000,
        MaximumStatisticsBytes = 64 * 1024 * 1024,
        MaximumResults = DefaultLimits.MaximumResults,
        MaximumResultBytes = DefaultLimits.MaximumResultBytes,
        MaximumCursorBytes = DefaultLimits.MaximumCursorBytes,
        MaximumStatementParameters = DefaultLimits.MaximumStatementParameters,
        MaximumRebuildStagingRows = 1_000_000,
        MaximumRebuildBytes = 1_073_741_824,
        MaximumTransientBytes = DefaultLimits.MaximumTransientBytes,
        MaximumWriteTime = TimeSpan.FromSeconds(30),
        MaximumQueryTime = DefaultLimits.QueryTimeout,
        MaximumConsistencyWait = DefaultLimits.ConsistencyWaitTimeout,
        MaximumInspectionTime = TimeSpan.FromSeconds(30),
        MaximumRebuildTime = TimeSpan.FromMinutes(5),
        MaximumQuarantinedOperations = 8,
    };

    internal static BaseTextExecutionLimits ExecutionLimits(BaseTextProviderCapability value) => new()
    {
        MaximumQueryNodes = value.MaximumQueryNodes, MaximumQueryDepth = value.MaximumQueryDepth, MaximumPhraseTerms = value.MaximumPhraseTerms, MaximumQueryBytes = value.MaximumQueryBytes,
        MaximumFilterNodes = value.MaximumFilterNodes, MaximumFilterDepth = value.MaximumFilterDepth, MaximumFilterLiterals = value.MaximumFilterLiterals, MaximumInValues = value.MaximumInValues,
        MaximumPrefixExpansions = value.MaximumPrefixExpansions, MaximumPrefixExpansionBytes = value.MaximumPrefixExpansionBytes, MaximumSecondaryOrderFields = value.MaximumSecondaryOrderFields,
        MaximumOrderingBytes = value.MaximumOrderingBytes, MaximumCandidates = value.MaximumCandidates, MaximumScoreProofBytes = value.MaximumScoreProofBytes,
        MaximumTokensPerField = Math.Min(value.MaximumTokensPerRecord, BaseTextAnalyzers.MaximumTokensPerField), MaximumNormalizedBytesPerField = value.MaximumNormalizedBytesPerField,
        MaximumNormalizedBytesPerRecord = value.MaximumNormalizedBytesPerRecord, MaximumResults = value.MaximumResults, MaximumResultBytes = value.MaximumResultBytes,
        MaximumCursorBytes = value.MaximumCursorBytes, MaximumStatementParameters = value.MaximumStatementParameters, MaximumTransientBytes = value.MaximumTransientBytes,
        QueryTimeout = value.MaximumQueryTime, ConsistencyWaitTimeout = value.MaximumConsistencyWait,
    };
}

/// <summary>Defines one searchable field in a text index.</summary>
public sealed record BaseTextIndexFieldDefinition
{
    /// <summary>Gets the stable serializer-bound field identity.</summary>
    public required string StableFieldId { get; init; }
    /// <summary>Gets the application property name.</summary>
    public required string ApplicationName { get; init; }
    /// <summary>Gets the serialized wire name.</summary>
    public required string WireName { get; init; }
    /// <summary>Gets the integer field weight from one through sixteen.</summary>
    public required int Weight { get; init; }
    /// <summary>Gets the field confidentiality classification.</summary>
    public required BaseFieldConfidentiality Confidentiality { get; init; }
    /// <summary>Gets the statically eligible search-influence audiences.</summary>
    public required ImmutableArray<HPDBaseEndpointAudience> StaticInfluenceAudiences { get; init; }
    /// <summary>Gets whether each request must provide a dynamic field-influence constraint.</summary>
    public required bool RequiresDynamicInfluenceConstraint { get; init; }
}

/// <summary>Defines one ordinary filter carrier in a text index.</summary>
public sealed record BaseTextIndexFilterFieldDefinition
{
    /// <summary>Gets the stable serializer-bound field identity.</summary>
    public required string StableFieldId { get; init; }
    /// <summary>Gets the application property name.</summary>
    public required string ApplicationName { get; init; }
    /// <summary>Gets the serialized wire name.</summary>
    public required string WireName { get; init; }
    /// <summary>Gets the closed filter value kind.</summary>
    public required BaseTextFilterValueKind ValueKind { get; init; }
}

/// <summary>Defines one immutable provider-neutral lexical index.</summary>
public sealed record BaseTextIndexDefinition
{
    /// <summary>Gets the stable index identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive definition version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning collection identity.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the generated endpoint audience.</summary>
    public required HPDBaseEndpointAudience Audience { get; init; }
    /// <summary>Gets searchable fields in declared order.</summary>
    public required ImmutableArray<BaseTextIndexFieldDefinition> Fields { get; init; }
    /// <summary>Gets filter fields in canonical stable-ID order.</summary>
    public required ImmutableArray<BaseTextIndexFilterFieldDefinition> FilterFields { get; init; }
    /// <summary>Gets the analyzer contract identity.</summary>
    public required string AnalyzerContractId { get; init; }
    /// <summary>Gets the exact analyzer receipt.</summary>
    public required ImmutableArray<byte> AnalyzerReceipt { get; init; }
    /// <summary>Gets the scoring contract identity.</summary>
    public required string ScoringContractId { get; init; }
    /// <summary>Gets the exact scoring receipt.</summary>
    public required ImmutableArray<byte> ScoringReceipt { get; init; }
    /// <summary>Gets the installed execution limits.</summary>
    public required BaseTextExecutionLimits Limits { get; init; }
    /// <summary>Gets the exact serializer graph checksum.</summary>
    public required ImmutableArray<byte> SerializerGraphChecksum { get; init; }
    /// <summary>Gets the canonical definition checksum.</summary>
    public required ImmutableArray<byte> DefinitionChecksum { get; init; }
}

/// <summary>Contains one generated typed lexical-index handle.</summary>
/// <typeparam name="T">The generated record payload type.</typeparam>
public sealed record BaseTextIndex<T>
{
    /// <summary>Gets the immutable logical definition.</summary>
    public required BaseTextIndexDefinition Definition { get; init; }
}

/// <summary>Declares one generated text index over serializer-bound fields.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class BaseTextIndexAttribute(string id) : Attribute
{
    /// <summary>Gets the stable index identity.</summary>
    public string Id { get; } = id;
    /// <summary>Gets the positive definition version.</summary>
    public int Version { get; set; } = 1;
    /// <summary>Gets or sets searchable property names in weight order.</summary>
    public string[] Fields { get; set; } = [];
    /// <summary>Gets or sets one weight per searchable property.</summary>
    public int[] Weights { get; set; } = [];
    /// <summary>Gets or sets ordinary filter-carrier property names.</summary>
    public string[] FilterFields { get; set; } = [];
    /// <summary>Gets or sets the generated endpoint audience.</summary>
    public HPDBaseEndpointAudience Audience { get; set; } = HPDBaseEndpointAudience.Application;
}

/// <summary>Lists stable lexical-search failure codes.</summary>
public static class BaseTextErrorCodes
{
    /// <summary>The installed text-search contract is invalid.</summary>
    public const string ContractInvalid = "base.text.contractInvalid";
    /// <summary>The lexical query is invalid.</summary>
    public const string QueryInvalid = "base.text.queryInvalid";
    /// <summary>The caller is not authorized to use the index.</summary>
    public const string Unauthorized = "base.text.unauthorized";
    /// <summary>The effective policy cannot be enforced before matching.</summary>
    public const string PolicyConstraintUnsupported = "base.text.policyConstraintUnsupported";
    /// <summary>The installed provider cannot supply the required capability.</summary>
    public const string CapabilityUnavailable = "base.text.capabilityUnavailable";
    /// <summary>The index is unavailable under its current generation.</summary>
    public const string IndexUnavailable = "base.text.indexUnavailable";
    /// <summary>The authorized index identity is not installed.</summary>
    public const string IndexNotFound = "base.text.indexNotFound";
    /// <summary>The index generation changed.</summary>
    public const string GenerationChanged = "base.text.generationChanged";
    /// <summary>The finite authoritative snapshot changed.</summary>
    public const string SnapshotChanged = "base.text.snapshotChanged";
    /// <summary>The continuation token is invalid.</summary>
    public const string CursorInvalid = "base.text.cursorInvalid";
    /// <summary>The authenticated continuation expired.</summary>
    public const string CursorExpired = "base.text.cursorExpired";
    /// <summary>The authenticated continuation belongs to different request authority.</summary>
    public const string CursorScopeMismatch = "base.text.cursorScopeMismatch";
    /// <summary>The requested consistency point is unavailable.</summary>
    public const string ConsistencyUnavailable = "base.text.consistencyUnavailable";
    /// <summary>The required retained history has been overtaken.</summary>
    public const string HistoryOvertaken = "base.text.historyOvertaken";
    /// <summary>A derived projection has an ordered journal gap.</summary>
    public const string DerivedProjectionGap = "base.text.derivedProjectionGap";
    /// <summary>A derived projection is corrupt.</summary>
    public const string DerivedProjectionCorrupt = "base.text.derivedProjectionCorrupt";
    /// <summary>The operation exceeded an installed bound.</summary>
    public const string BudgetExceeded = "base.text.budgetExceeded";
    /// <summary>The provider returned invalid evidence.</summary>
    public const string ProviderContractInvalid = "base.text.providerContractInvalid";
    /// <summary>The provider returned invalid completeness evidence.</summary>
    public const string CompletenessEvidenceInvalid = "base.text.completenessEvidenceInvalid";
    /// <summary>The operation timed out.</summary>
    public const string Timeout = "base.text.timeout";
    /// <summary>The index requires rebuilding.</summary>
    public const string RebuildRequired = "base.text.rebuildRequired";
    /// <summary>The maintenance commit outcome is indeterminate.</summary>
    public const string CommitIndeterminate = "base.text.commitIndeterminate";
    /// <summary>The InMemory authority changed during every bounded fresh-root rebuild attempt.</summary>
    public const string InMemoryGenerationChanged = "base.text.inMemory.generationChanged";
}
