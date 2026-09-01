using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Seals and validates logical-index provider capability authority.</summary>
public static class BaseLogicalIndexProviderContract
{
    /// <summary>Gets the immutable provider-certification protocol.</summary>
    public const string Protocol = "hpd.base.logicalIndex.providerCertification.v2";

    /// <summary>Gets certification case IDs in canonical registry order.</summary>
    public static ImmutableArray<string> CaseIds { get; } =
    [
        "empty-directory", "membership", "equality-key", "comparator-order",
        "insert", "update-key-move", "delete", "unique-final-overlay",
        "duplicate-conflict", "point-hit", "point-miss", "point-policy",
        "point-generation-conflict", "scan-fallback", "maximum", "maximum-plus-one",
        "hostile-member-set", "hostile-result-ownership",
    ];

    /// <summary>Creates the exact built-in production capability.</summary>
    public static BaseLogicalIndexProviderCapability BuiltInCapability() => SealCapability(new()
    {
        Supported = true,
        EqualityKeyCodecVersion = 1,
        AccessShapes = [BaseIndexAccessShape.LogicalIndexPoint, BaseIndexAccessShape.CollectionGenerationScan],
        MaximumIndexesPerCollection = 8,
        MaximumPartsPerIndex = 8,
        MaximumPredicateNodesPerIndex = 128,
        MaximumCanonicalKeyBytes = 65_536,
        MaximumIndexedRecordsPerCollection = 1_000_000,
        MaximumPostingsPerIndex = 1_000_000,
        MaximumPostingRecordsPerKey = 1_000_000,
        MaximumPostingsPerStore = 8_000_000,
        MaximumDirectoryBytesPerIndex = 268_435_456,
        MaximumDirectoryBytesPerStore = 1_073_741_824,
        MaximumDirectoryPredicateEvaluationsPerPublication = 1_000_000,
        MaximumDirectoryKeyBytesPerIndex = 16_777_216,
        MaximumDirectoryTransientBytesPerOperation = 67_108_864,
        Checksum = [],
    });

    /// <summary>Creates the closed reduced capability used only by executable certification.</summary>
    public static BaseLogicalIndexProviderCapability BoundedCertificationCapability() => SealCapability(new()
    {
        Supported = true,
        EqualityKeyCodecVersion = 1,
        AccessShapes = [BaseIndexAccessShape.LogicalIndexPoint, BaseIndexAccessShape.CollectionGenerationScan],
        MaximumIndexesPerCollection = 2,
        MaximumPartsPerIndex = 2,
        MaximumPredicateNodesPerIndex = 3,
        MaximumCanonicalKeyBytes = 64,
        MaximumIndexedRecordsPerCollection = 4,
        MaximumPostingsPerIndex = 4,
        MaximumPostingRecordsPerKey = 4,
        MaximumPostingsPerStore = 8,
        MaximumDirectoryBytesPerIndex = 4_096,
        MaximumDirectoryBytesPerStore = 8_192,
        MaximumDirectoryPredicateEvaluationsPerPublication = 4,
        MaximumDirectoryKeyBytesPerIndex = 16_384,
        MaximumDirectoryTransientBytesPerOperation = 32_768,
        Checksum = [],
    });

    /// <summary>Creates the closed unsupported capability.</summary>
    public static BaseLogicalIndexProviderCapability UnsupportedCapability() => SealCapability(new()
    {
        Supported = false,
        EqualityKeyCodecVersion = 0,
        AccessShapes = [],
        MaximumIndexesPerCollection = 0,
        MaximumPartsPerIndex = 0,
        MaximumPredicateNodesPerIndex = 0,
        MaximumCanonicalKeyBytes = 0,
        MaximumIndexedRecordsPerCollection = 0,
        MaximumPostingsPerIndex = 0,
        MaximumPostingRecordsPerKey = 0,
        MaximumPostingsPerStore = 0,
        MaximumDirectoryBytesPerIndex = 0,
        MaximumDirectoryBytesPerStore = 0,
        MaximumDirectoryPredicateEvaluationsPerPublication = 0,
        MaximumDirectoryKeyBytesPerIndex = 0,
        MaximumDirectoryTransientBytesPerOperation = 0,
        Checksum = [],
    });

    /// <summary>Seals one deeply owned capability.</summary>
    /// <param name="value">The capability to normalize and checksum.</param>
    /// <returns>The sealed capability.</returns>
    public static BaseLogicalIndexProviderCapability SealCapability(
        BaseLogicalIndexProviderCapability value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BaseLogicalIndexProviderCapability owned = CloneCapability(value) with { Checksum = [] };
        if (!ValidMembers(owned))
            throw new ArgumentException("base.logicalIndex.providerCapabilityInvalid", nameof(value));
        return owned with { Checksum = CapabilityChecksum(owned) };
    }

    /// <summary>Returns whether one capability has exact members and checksum.</summary>
    public static bool ValidateCapability(BaseLogicalIndexProviderCapability value)
    {
        if (value is null || !ValidMembers(value) || value.Checksum.Length != 32) return false;
        ImmutableArray<byte> expected = CapabilityChecksum(value with { Checksum = [] });
        return CryptographicOperations.FixedTimeEquals(expected.AsSpan(), value.Checksum.AsSpan());
    }

    /// <summary>Returns a deeply owned capability.</summary>
    public static BaseLogicalIndexProviderCapability CloneCapability(
        BaseLogicalIndexProviderCapability value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value with
        {
            AccessShapes = value.AccessShapes.ToArray().ToImmutableArray(),
            Checksum = value.Checksum.ToArray().ToImmutableArray(),
        };
    }

    /// <summary>Computes the canonical capability checksum.</summary>
    public static ImmutableArray<byte> CapabilityChecksum(BaseLogicalIndexProviderCapability value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, "base.logicalIndex.providerCapability.v2");
        AppendBoolean(hash, value.Supported);
        AppendInt32(hash, value.EqualityKeyCodecVersion);
        AppendInt32(hash, value.AccessShapes.Length);
        foreach (BaseIndexAccessShape shape in value.AccessShapes) AppendInt32(hash, (int)shape);
        AppendInt32(hash, value.MaximumIndexesPerCollection);
        AppendInt32(hash, value.MaximumPartsPerIndex);
        AppendInt32(hash, value.MaximumPredicateNodesPerIndex);
        AppendInt32(hash, value.MaximumCanonicalKeyBytes);
        AppendInt64(hash, value.MaximumIndexedRecordsPerCollection);
        AppendInt64(hash, value.MaximumPostingsPerIndex);
        AppendInt32(hash, value.MaximumPostingRecordsPerKey);
        AppendInt64(hash, value.MaximumPostingsPerStore);
        AppendInt64(hash, value.MaximumDirectoryBytesPerIndex);
        AppendInt64(hash, value.MaximumDirectoryBytesPerStore);
        AppendInt64(hash, value.MaximumDirectoryPredicateEvaluationsPerPublication);
        AppendInt64(hash, value.MaximumDirectoryKeyBytesPerIndex);
        AppendInt64(hash, value.MaximumDirectoryTransientBytesPerOperation);
        return hash.GetHashAndReset().ToImmutableArray();
    }

    /// <summary>Creates one closed unsupported provider profile.</summary>
    /// <param name="storeProviderKind">The selected store-provider kind.</param>
    /// <returns>The sealed unsupported profile.</returns>
    public static BaseLogicalIndexProviderProfile UnsupportedProfile(string storeProviderKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeProviderKind);
        BaseLogicalIndexProviderCapability capability = UnsupportedCapability();
        var profile = new BaseLogicalIndexProviderProfile
        {
            Supported = false,
            ProviderId = string.Empty,
            ProviderVersion = 0,
            StoreProviderKind = new string(storeProviderKind.AsSpan()),
            StoreProviderProtocolVersion = HPDBaseStoreProviderFactory.ProtocolVersion,
            Capability = capability,
            NativeDependencyReceipts = [],
            ContractChecksum = ContractChecksum(),
            ExecutedReportChecksum = [],
            Checksum = [],
        };
        return profile with { Checksum = ProfileChecksum(profile) };
    }

    /// <summary>Seals a supported profile from a complete harness-validated report.</summary>
    /// <param name="report">The executed certification report.</param>
    /// <param name="capability">The exact production capability exercised by the report.</param>
    /// <param name="nativeDependencyReceipts">Sorted immutable native dependency receipts.</param>
    /// <returns>The sealed supported profile.</returns>
    public static BaseLogicalIndexProviderProfile SealSupportedProfile(
        BaseLogicalIndexCertificationReport report,
        BaseLogicalIndexProviderCapability capability,
        IEnumerable<string>? nativeDependencyReceipts = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(capability);
        BaseLogicalIndexProviderCapability production = SealCapability(capability);
        BaseLogicalIndexCertificationReport executed = SealReport(report);
        string[] dependencies = (nativeDependencyReceipts ?? [])
            .Select(static value => new string(value.AsSpan())).Order(StringComparer.Ordinal).ToArray();
        if (!production.Supported
            || !CryptographicOperations.FixedTimeEquals(
                production.Checksum.AsSpan(), BuiltInCapability().Checksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(
                executed.ProductionCapabilityChecksum.AsSpan(), production.Checksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(
                executed.BoundedCertificationCapabilityChecksum.AsSpan(),
                BoundedCertificationCapability().Checksum.AsSpan())
            || dependencies.Any(string.IsNullOrWhiteSpace)
            || dependencies.Distinct(StringComparer.Ordinal).Count() != dependencies.Length
            || !ReportOutcomesAreSuccessful(executed))
            throw new ArgumentException("base.logicalIndex.certificationReportInvalid", nameof(report));
        var profile = new BaseLogicalIndexProviderProfile
        {
            Supported = true,
            ProviderId = new string(executed.ProviderId.AsSpan()),
            ProviderVersion = executed.ProviderVersion,
            StoreProviderKind = new string(executed.StoreProviderKind.AsSpan()),
            StoreProviderProtocolVersion = executed.StoreProviderProtocolVersion,
            Capability = CloneCapability(production),
            NativeDependencyReceipts = dependencies.ToImmutableArray(),
            ContractChecksum = ContractChecksum(),
            ExecutedReportChecksum = executed.Checksum.ToArray().ToImmutableArray(),
            Checksum = [],
        };
        return profile with { Checksum = ProfileChecksum(profile) };
    }

    /// <summary>Gets the immutable expected status and safe error for one certification case.</summary>
    public static (OperationStatus Status, string? ErrorCode) ExpectedOutcome(string caseId) => caseId switch
    {
        "duplicate-conflict" => (OperationStatus.Conflict, BaseSchemaErrorCodes.UniqueConstraintViolated),
        "point-generation-conflict" => (OperationStatus.Conflict, BaseSchemaErrorCodes.TransactionConflict),
        "maximum-plus-one" => (OperationStatus.CapabilityUnavailable, BaseSchemaErrorCodes.CapabilityUnavailable),
        "hostile-member-set" or "hostile-result-ownership" =>
            (OperationStatus.StoreError, BaseSchemaErrorCodes.ProviderEvidenceInvalid),
        _ when CaseIds.Contains(caseId, StringComparer.Ordinal) => (OperationStatus.Ok, null),
        _ => throw new ArgumentOutOfRangeException(nameof(caseId)),
    };

    /// <summary>Returns a deeply owned provider profile.</summary>
    /// <param name="value">The profile to clone.</param>
    /// <returns>The deeply owned profile.</returns>
    public static BaseLogicalIndexProviderProfile CloneProfile(BaseLogicalIndexProviderProfile value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value with
        {
            ProviderId = new string(value.ProviderId.AsSpan()),
            StoreProviderKind = new string(value.StoreProviderKind.AsSpan()),
            Capability = CloneCapability(value.Capability),
            NativeDependencyReceipts = value.NativeDependencyReceipts
                .Select(static item => new string(item.AsSpan())).ToImmutableArray(),
            ContractChecksum = value.ContractChecksum.ToArray().ToImmutableArray(),
            ExecutedReportChecksum = value.ExecutedReportChecksum.ToArray().ToImmutableArray(),
            Checksum = value.Checksum.ToArray().ToImmutableArray(),
        };
    }

    /// <summary>Returns whether one provider profile has exact canonical authority.</summary>
    /// <param name="value">The profile to validate.</param>
    /// <returns><see langword="true"/> when the profile is valid.</returns>
    public static bool ValidateProfile(BaseLogicalIndexProviderProfile value)
    {
        if (value is null || !ValidateCapability(value.Capability)
            || value.StoreProviderProtocolVersion != HPDBaseStoreProviderFactory.ProtocolVersion
            || string.IsNullOrWhiteSpace(value.StoreProviderKind)
            || value.NativeDependencyReceipts.IsDefault
            || !value.NativeDependencyReceipts.SequenceEqual(
                value.NativeDependencyReceipts.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || value.NativeDependencyReceipts.Distinct(StringComparer.Ordinal).Count()
                != value.NativeDependencyReceipts.Length
            || value.ContractChecksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(
                value.ContractChecksum.AsSpan(), ContractChecksum().AsSpan())
            || value.Checksum.Length != 32)
            return false;
        if (!value.Supported && (value.ProviderId.Length != 0 || value.ProviderVersion != 0
            || value.Capability.Supported || !value.NativeDependencyReceipts.IsEmpty
            || !value.ExecutedReportChecksum.IsEmpty))
            return false;
        if (value.Supported && (string.IsNullOrWhiteSpace(value.ProviderId)
            || value.ProviderVersion <= 0 || !value.Capability.Supported
            || value.ExecutedReportChecksum.Length != 32))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            ProfileChecksum(value with { Checksum = [] }).AsSpan(), value.Checksum.AsSpan());
    }

    /// <summary>Computes the immutable certification-contract checksum.</summary>
    public static ImmutableArray<byte> ContractChecksum()
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, "base.logicalIndex.providerCertificationContract.v2");
        AppendText(hash, Protocol);
        AppendInt32(hash, CaseIds.Length);
        foreach (string id in CaseIds) AppendText(hash, id);
        AppendText(hash,
            "profile:supported,providerId,providerVersion,storeKind,storeProtocol,capability,dependencies,contract,report,checksum;"
            + "report:providerId,providerVersion,storeKind,storeProtocol,production,bounded,cases,contract,checksum;"
            + "case:id,ordinal,status,error,accounting,beforeMember,afterMember,beforePublication,afterPublication,evidence;"
            + "accounting:records,predicates,keys,keyBytes,postingKeys,postings,entries,comparisons,evidence,directory,transient");
        return hash.GetHashAndReset().ToImmutableArray();
    }

    /// <summary>Computes one canonical provider-profile checksum.</summary>
    /// <param name="value">The profile authority.</param>
    /// <returns>The checksum.</returns>
    public static ImmutableArray<byte> ProfileChecksum(BaseLogicalIndexProviderProfile value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, "base.logicalIndex.providerProfile.v2");
        AppendBoolean(hash, value.Supported); AppendText(hash, value.ProviderId);
        AppendInt32(hash, value.ProviderVersion); AppendText(hash, value.StoreProviderKind);
        AppendInt32(hash, value.StoreProviderProtocolVersion); AppendBytes(hash, value.Capability.Checksum.AsSpan());
        AppendInt32(hash, value.NativeDependencyReceipts.Length);
        foreach (string receipt in value.NativeDependencyReceipts) AppendText(hash, receipt);
        AppendBytes(hash, value.ContractChecksum.AsSpan());
        AppendBytes(hash, value.ExecutedReportChecksum.AsSpan());
        return hash.GetHashAndReset().ToImmutableArray();
    }

    /// <summary>Seals one deeply owned logical-index certification report.</summary>
    /// <param name="value">The completed certification report.</param>
    /// <returns>The sealed report.</returns>
    public static BaseLogicalIndexCertificationReport SealReport(
        BaseLogicalIndexCertificationReport value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BaseLogicalIndexCertificationReport owned = CloneReport(value) with { Checksum = [] };
        if (!ValidReportMembers(owned))
            throw new ArgumentException("base.logicalIndex.certificationReportInvalid", nameof(value));
        return owned with { Checksum = ReportChecksum(owned) };
    }

    /// <summary>Returns whether one certification report has complete canonical authority.</summary>
    /// <param name="value">The report to validate.</param>
    /// <returns><see langword="true"/> when the report is valid.</returns>
    public static bool ValidateReport(BaseLogicalIndexCertificationReport value)
    {
        if (value is null || value.Checksum.Length != 32 || !ValidReportMembers(value)) return false;
        return CryptographicOperations.FixedTimeEquals(
            ReportChecksum(value with { Checksum = [] }).AsSpan(), value.Checksum.AsSpan());
    }

    /// <summary>Returns a deeply owned certification report.</summary>
    /// <param name="value">The report to clone.</param>
    /// <returns>The deeply owned report.</returns>
    public static BaseLogicalIndexCertificationReport CloneReport(
        BaseLogicalIndexCertificationReport value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value with
        {
            ProviderId = new string(value.ProviderId.AsSpan()),
            StoreProviderKind = new string(value.StoreProviderKind.AsSpan()),
            ProductionCapabilityChecksum = value.ProductionCapabilityChecksum.ToArray().ToImmutableArray(),
            BoundedCertificationCapabilityChecksum = value.BoundedCertificationCapabilityChecksum.ToArray().ToImmutableArray(),
            Cases = value.Cases.Select(CloneCase).ToImmutableArray(),
            ContractChecksum = value.ContractChecksum.ToArray().ToImmutableArray(),
            Checksum = value.Checksum.ToArray().ToImmutableArray(),
        };
    }

    /// <summary>Computes one canonical certification-report checksum.</summary>
    /// <param name="value">The report authority.</param>
    /// <returns>The checksum.</returns>
    public static ImmutableArray<byte> ReportChecksum(BaseLogicalIndexCertificationReport value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, "base.logicalIndex.certificationReport.v2");
        AppendText(hash, value.ProviderId); AppendInt32(hash, value.ProviderVersion);
        AppendText(hash, value.StoreProviderKind); AppendInt32(hash, value.StoreProviderProtocolVersion);
        AppendBytes(hash, value.ProductionCapabilityChecksum.AsSpan());
        AppendBytes(hash, value.BoundedCertificationCapabilityChecksum.AsSpan());
        AppendInt32(hash, value.Cases.Length);
        foreach (BaseLogicalIndexCertificationCaseResult item in value.Cases)
            AppendBytes(hash, CaseChecksum(item).AsSpan());
        AppendBytes(hash, value.ContractChecksum.AsSpan());
        return hash.GetHashAndReset().ToImmutableArray();
    }

    /// <summary>Computes one canonical certification-case checksum.</summary>
    /// <param name="value">The observed case result.</param>
    /// <returns>The checksum.</returns>
    public static ImmutableArray<byte> CaseChecksum(BaseLogicalIndexCertificationCaseResult value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, "base.logicalIndex.certificationCase.v2");
        AppendText(hash, value.Id); AppendInt32(hash, value.Ordinal);
        AppendInt32(hash, (int)value.ObservedStatus);
        AppendBoolean(hash, value.ObservedErrorCode is not null);
        if (value.ObservedErrorCode is not null) AppendText(hash, value.ObservedErrorCode);
        AppendAccounting(hash, value.Accounting);
        AppendBytes(hash, value.BeforeMemberSetChecksum.AsSpan());
        AppendBytes(hash, value.AfterMemberSetChecksum.AsSpan());
        AppendBytes(hash, value.BeforePublicationChecksum.AsSpan());
        AppendBytes(hash, value.AfterPublicationChecksum.AsSpan());
        AppendBytes(hash, value.EvidenceChecksum.AsSpan());
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static bool ValidReportMembers(BaseLogicalIndexCertificationReport value)
    {
        if (string.IsNullOrWhiteSpace(value.ProviderId) || value.ProviderVersion <= 0
            || string.IsNullOrWhiteSpace(value.StoreProviderKind)
            || value.StoreProviderProtocolVersion != HPDBaseStoreProviderFactory.ProtocolVersion
            || value.ProductionCapabilityChecksum.Length != 32
            || value.BoundedCertificationCapabilityChecksum.Length != 32
            || value.Cases.IsDefault || value.Cases.Length != CaseIds.Length
            || value.ContractChecksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(
                value.ContractChecksum.AsSpan(), ContractChecksum().AsSpan()))
            return false;
        for (int index = 0; index < value.Cases.Length; index++)
            if (!ValidCase(value.Cases[index], CaseIds[index], index)) return false;
        return true;
    }

    private static bool ReportOutcomesAreSuccessful(BaseLogicalIndexCertificationReport value)
    {
        for (int ordinal = 0; ordinal < CaseIds.Length; ordinal++)
        {
            (OperationStatus status, string? error) = ExpectedOutcome(CaseIds[ordinal]);
            BaseLogicalIndexCertificationCaseResult observed = value.Cases[ordinal];
            if (observed.ObservedStatus != status
                || !string.Equals(observed.ObservedErrorCode, error, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static bool ValidCase(BaseLogicalIndexCertificationCaseResult value, string expectedId, int ordinal) =>
        value is not null && string.Equals(value.Id, expectedId, StringComparison.Ordinal)
        && value.Ordinal == ordinal && Enum.IsDefined(value.ObservedStatus)
        && (value.ObservedErrorCode is null || !string.IsNullOrWhiteSpace(value.ObservedErrorCode))
        && ValidAccounting(value.Accounting)
        && value.BeforeMemberSetChecksum.Length == 32 && value.AfterMemberSetChecksum.Length == 32
        && value.BeforePublicationChecksum.Length == 32 && value.AfterPublicationChecksum.Length == 32
        && value.EvidenceChecksum.Length == 32;

    private static bool ValidAccounting(BaseLogicalIndexCertificationAccounting value) =>
        value is not null && value.Records >= 0 && value.PredicateEvaluations >= 0
        && value.Keys >= 0 && value.KeyBytes >= 0 && value.PostingKeys >= 0
        && value.Postings >= 0 && value.ComparatorEntries >= 0 && value.Comparisons >= 0
        && value.EvidenceBytes >= 0 && value.RetainedDirectoryBytes >= 0 && value.TransientBytes >= 0;

    private static BaseLogicalIndexCertificationCaseResult CloneCase(
        BaseLogicalIndexCertificationCaseResult value) => value with
    {
        Id = new string(value.Id.AsSpan()),
        ObservedErrorCode = value.ObservedErrorCode is null
            ? null : new string(value.ObservedErrorCode.AsSpan()),
        Accounting = value.Accounting with { },
        BeforeMemberSetChecksum = value.BeforeMemberSetChecksum.ToArray().ToImmutableArray(),
        AfterMemberSetChecksum = value.AfterMemberSetChecksum.ToArray().ToImmutableArray(),
        BeforePublicationChecksum = value.BeforePublicationChecksum.ToArray().ToImmutableArray(),
        AfterPublicationChecksum = value.AfterPublicationChecksum.ToArray().ToImmutableArray(),
        EvidenceChecksum = value.EvidenceChecksum.ToArray().ToImmutableArray(),
    };

    private static void AppendAccounting(
        IncrementalHash hash, BaseLogicalIndexCertificationAccounting value)
    {
        AppendInt64(hash, value.Records); AppendInt64(hash, value.PredicateEvaluations);
        AppendInt64(hash, value.Keys); AppendInt64(hash, value.KeyBytes);
        AppendInt64(hash, value.PostingKeys); AppendInt64(hash, value.Postings);
        AppendInt64(hash, value.ComparatorEntries); AppendInt64(hash, value.Comparisons);
        AppendInt64(hash, value.EvidenceBytes); AppendInt64(hash, value.RetainedDirectoryBytes);
        AppendInt64(hash, value.TransientBytes);
    }

    private static bool ValidMembers(BaseLogicalIndexProviderCapability value)
    {
        if (value.AccessShapes.IsDefault) return false;
        bool empty = value.EqualityKeyCodecVersion == 0 && value.AccessShapes.IsEmpty
            && value.MaximumIndexesPerCollection == 0 && value.MaximumPartsPerIndex == 0
            && value.MaximumPredicateNodesPerIndex == 0 && value.MaximumCanonicalKeyBytes == 0
            && value.MaximumIndexedRecordsPerCollection == 0 && value.MaximumPostingsPerIndex == 0
            && value.MaximumPostingRecordsPerKey == 0 && value.MaximumPostingsPerStore == 0
            && value.MaximumDirectoryBytesPerIndex == 0
            && value.MaximumDirectoryBytesPerStore == 0
            && value.MaximumDirectoryPredicateEvaluationsPerPublication == 0
            && value.MaximumDirectoryKeyBytesPerIndex == 0
            && value.MaximumDirectoryTransientBytesPerOperation == 0;
        if (!value.Supported) return empty;
        return value.EqualityKeyCodecVersion == 1
            && value.AccessShapes.SequenceEqual(
                [BaseIndexAccessShape.LogicalIndexPoint, BaseIndexAccessShape.CollectionGenerationScan])
            && value.MaximumIndexesPerCollection is >= 1 and <= 8
            && value.MaximumPartsPerIndex is >= 1 and <= 8
            && value.MaximumPredicateNodesPerIndex is >= 1 and <= 128
            && value.MaximumCanonicalKeyBytes is >= 1 and <= 65_536
            && value.MaximumIndexedRecordsPerCollection > 0
            && value.MaximumPostingsPerIndex > 0
            && value.MaximumPostingRecordsPerKey > 0
            && value.MaximumPostingsPerStore >= value.MaximumPostingsPerIndex
            && value.MaximumDirectoryBytesPerIndex > 0
            && value.MaximumDirectoryBytesPerStore >= value.MaximumDirectoryBytesPerIndex
            && value.MaximumDirectoryPredicateEvaluationsPerPublication > 0
            && value.MaximumDirectoryKeyBytesPerIndex > 0
            && value.MaximumDirectoryTransientBytesPerOperation > 0;
    }

    internal static void AppendBoolean(IncrementalHash hash, bool value) =>
        hash.AppendData(value ? [(byte)1] : [(byte)0]);

    internal static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value); hash.AppendData(bytes);
    }

    internal static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes);
    }

    internal static void AppendBytes(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        AppendInt32(hash, value.Length); hash.AppendData(value);
    }

    internal static void AppendText(IncrementalHash hash, string value) =>
        AppendBytes(hash, Encoding.UTF8.GetBytes(value));
}
