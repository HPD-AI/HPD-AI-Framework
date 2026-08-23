using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Describes the certified provider envelope for semantic activation authority.</summary>
public sealed record BaseSemanticActivationCapability
{
    /// <summary>Gets whether semantic activation identity is supported.</summary>
    public required bool Supported { get; init; }
    /// <summary>Gets whether semantic state and module mutations share one transaction.</summary>
    public required bool SameTransactionModuleMutationSupported { get; init; }
    /// <summary>Gets whether restore recovery floors are supported.</summary>
    public required bool RestoreRecoveryFloorsSupported { get; init; }
    /// <summary>Gets whether bounded semantic maintenance is executable.</summary>
    public required bool MaintenanceSupported { get; init; }
    /// <summary>Gets the maximum installed definitions.</summary>
    public required int MaximumDefinitions { get; init; }
    /// <summary>Gets the maximum canonical key bytes.</summary>
    public required int MaximumKeyBytes { get; init; }
    /// <summary>Gets the maximum live slots.</summary>
    public required long MaximumLiveSlots { get; init; }
    /// <summary>Gets the maximum retired slots.</summary>
    public required long MaximumRetiredSlots { get; init; }
    /// <summary>Gets the maximum compacted absence markers.</summary>
    public required long MaximumAbsenceMarkers { get; init; }
    /// <summary>Gets the maximum semantic operations per transaction.</summary>
    public required int MaximumOperationsPerTransaction { get; init; }
    /// <summary>Gets the maximum scope-directory reads.</summary>
    public required int MaximumScopeDirectoryReads { get; init; }
    /// <summary>Gets the maximum slot reads.</summary>
    public required int MaximumSlotReads { get; init; }
    /// <summary>Gets the maximum activation reads.</summary>
    public required int MaximumActivationReads { get; init; }
    /// <summary>Gets the maximum read intervals.</summary>
    public required int MaximumReadIntervals { get; init; }
    /// <summary>Gets the maximum index operations.</summary>
    public required int MaximumIndexOperations { get; init; }
    /// <summary>Gets the maximum activation bytes.</summary>
    public required long MaximumActivationBytes { get; init; }
    /// <summary>Gets the maximum scope-directory bytes.</summary>
    public required long MaximumScopeDirectoryBytes { get; init; }
    /// <summary>Gets the maximum evidence bytes.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the maximum receipt bytes.</summary>
    public required long MaximumReceiptBytes { get; init; }
    /// <summary>Gets the maximum retained transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the maximum maintenance page size.</summary>
    public required int MaximumMaintenancePageSize { get; init; }
    /// <summary>Gets the provider deadline envelope.</summary>
    public required BaseSemanticActivationDeadlineCapability Deadlines { get; init; }
    /// <summary>Gets supported whole-store backup modes.</summary>
    public required ImmutableArray<BaseActivationBackupMode> BackupModes { get; init; }
    /// <summary>Gets supported restore modes.</summary>
    public required ImmutableArray<BaseActivationRestoreMode> RestoreModes { get; init; }
    /// <summary>Gets the canonical capability checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Validates and creates the closed semantic activation capability.</summary>
public static class BaseSemanticActivationCapabilityContract
{
    /// <summary>Gets the canonical unsupported semantic activation envelope.</summary>
    public static BaseSemanticActivationCapability Unsupported()
    {
        BaseSemanticActivationCapability value = new()
        {
        Supported = false, SameTransactionModuleMutationSupported = false, RestoreRecoveryFloorsSupported = false,
        MaintenanceSupported = false,
        MaximumDefinitions = 0, MaximumKeyBytes = 0, MaximumLiveSlots = 0, MaximumRetiredSlots = 0,
        MaximumAbsenceMarkers = 0, MaximumOperationsPerTransaction = 0, MaximumScopeDirectoryReads = 0,
        MaximumSlotReads = 0, MaximumActivationReads = 0, MaximumReadIntervals = 0, MaximumIndexOperations = 0,
        MaximumActivationBytes = 0, MaximumScopeDirectoryBytes = 0, MaximumEvidenceBytes = 0,
        MaximumReceiptBytes = 0, MaximumTransientBytes = 0, MaximumMaintenancePageSize = 0,
        Deadlines = new BaseSemanticActivationDeadlineCapability
        {
            AcquisitionTimeout = TimeSpan.Zero, TransactionTimeout = TimeSpan.Zero,
            CommitObservationTimeout = TimeSpan.Zero, ReceiptResolutionTimeout = TimeSpan.Zero,
            MaintenanceTimeout = TimeSpan.Zero, QuarantineRetentionTimeout = TimeSpan.Zero,
        }, BackupModes = [], RestoreModes = [],
            Checksum = [],
        };
        return value with { Checksum = Checksum(value) };
    }

    /// <summary>Returns a deeply owned capability copy.</summary>
    public static BaseSemanticActivationCapability Clone(BaseSemanticActivationCapability value) => value with
    {
        Deadlines = value.Deadlines with { }, BackupModes = value.BackupModes.ToArray().ToImmutableArray(),
        RestoreModes = value.RestoreModes.ToArray().ToImmutableArray(), Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };

    /// <summary>Gets the platform maximum provider envelope.</summary>
    public static BaseSemanticActivationCapability BuiltIn(bool durable)
    {
        BaseSemanticActivationCapability value = new()
        {
        Supported = true, SameTransactionModuleMutationSupported = true,
        MaintenanceSupported = durable,
        RestoreRecoveryFloorsSupported = durable, MaximumDefinitions = 4096,
        MaximumKeyBytes = 1024, MaximumLiveSlots = 1_000_000,
        MaximumRetiredSlots = 1_000_000, MaximumAbsenceMarkers = 1_000_000,
        MaximumOperationsPerTransaction = 1, MaximumScopeDirectoryReads = 1,
        MaximumSlotReads = 1, MaximumActivationReads = 1, MaximumReadIntervals = 4096,
        MaximumIndexOperations = 8192, MaximumActivationBytes = 1_048_576,
        MaximumScopeDirectoryBytes = 65_536, MaximumEvidenceBytes = 1_048_576,
        MaximumReceiptBytes = 1_048_576, MaximumTransientBytes = 8_388_608,
        MaximumMaintenancePageSize = durable ? 256 : 0,
        Deadlines = new BaseSemanticActivationDeadlineCapability
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(30),
            CommitObservationTimeout = TimeSpan.FromSeconds(30), ReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
            MaintenanceTimeout = TimeSpan.FromMinutes(5), QuarantineRetentionTimeout = TimeSpan.FromMinutes(5),
        },
        BackupModes = durable ? [BaseActivationBackupMode.WholeStoreAtomic] : [],
        RestoreModes = durable ? [BaseActivationRestoreMode.InPlaceRecovery] : [],
            Checksum = [],
        };
        return value with { Checksum = Checksum(value) };
    }

    /// <summary>Computes the purpose-bound canonical checksum of the complete capability.</summary>
    public static ImmutableArray<byte> Checksum(BaseSemanticActivationCapability value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.capability.v1\0"u8);
        Bool(value.Supported); Bool(value.SameTransactionModuleMutationSupported); Bool(value.RestoreRecoveryFloorsSupported); Bool(value.MaintenanceSupported);
        Int(value.MaximumDefinitions); Int(value.MaximumKeyBytes); Long(value.MaximumLiveSlots); Long(value.MaximumRetiredSlots); Long(value.MaximumAbsenceMarkers);
        Int(value.MaximumOperationsPerTransaction); Int(value.MaximumScopeDirectoryReads); Int(value.MaximumSlotReads); Int(value.MaximumActivationReads);
        Int(value.MaximumReadIntervals); Int(value.MaximumIndexOperations); Long(value.MaximumActivationBytes); Long(value.MaximumScopeDirectoryBytes);
        Long(value.MaximumEvidenceBytes); Long(value.MaximumReceiptBytes); Long(value.MaximumTransientBytes); Int(value.MaximumMaintenancePageSize);
        Long(value.Deadlines.AcquisitionTimeout.Ticks); Long(value.Deadlines.TransactionTimeout.Ticks); Long(value.Deadlines.CommitObservationTimeout.Ticks);
        Long(value.Deadlines.ReceiptResolutionTimeout.Ticks); Long(value.Deadlines.MaintenanceTimeout.Ticks); Long(value.Deadlines.QuarantineRetentionTimeout.Ticks);
        Enums(value.BackupModes.Select(static item => (int)item)); Enums(value.RestoreModes.Select(static item => (int)item));
        return hash.GetHashAndReset().ToImmutableArray();

        void Bool(bool item) => hash.AppendData(item ? [1] : [0]);
        void Int(int item) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, item); hash.AppendData(bytes); }
        void Long(long item) { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, item); hash.AppendData(bytes); }
        void Enums(IEnumerable<int> items) { int[] array = items.ToArray(); Int(array.Length); foreach (int item in array) Int(item); }
    }

    /// <summary>Returns whether a capability is a closed valid safety envelope.</summary>
    public static bool IsValid(BaseSemanticActivationCapability? value) => value is not null && value.Checksum.Length == 32
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Checksum(value).AsSpan(), value.Checksum.AsSpan())
        && (!value.Supported ? IsUnsupported(value) : value.SameTransactionModuleMutationSupported
        && value.MaximumDefinitions is >= 1 and <= 4096
        && value.MaximumKeyBytes is >= 1 and <= 1024
        && value.MaximumLiveSlots is >= 1 and <= 1_000_000
        && value.MaximumRetiredSlots is >= 1 and <= 1_000_000
        && value.MaximumAbsenceMarkers is >= 1 and <= 1_000_000
        && value.MaximumOperationsPerTransaction == 1
        && value.MaximumScopeDirectoryReads == 1 && value.MaximumSlotReads == 1
        && value.MaximumActivationReads == 1
        && value.MaximumReadIntervals is >= 1 and <= 4096
        && value.MaximumIndexOperations is >= 1 and <= 8192
        && value.MaximumActivationBytes is >= 1 and <= 1_048_576
        && value.MaximumScopeDirectoryBytes is >= 1 and <= 65_536
        && value.MaximumEvidenceBytes is >= 1 and <= 1_048_576
        && value.MaximumReceiptBytes is >= 1 and <= 1_048_576
        && value.MaximumTransientBytes is >= 1 and <= 8_388_608
        && (!value.MaintenanceSupported ? value.MaximumMaintenancePageSize == 0
            : value.MaximumMaintenancePageSize is >= 1 and <= 256)
        && ValidDeadlines(value.Deadlines)
        && value.BackupModes.Distinct().Count() == value.BackupModes.Length
        && value.BackupModes.All(Enum.IsDefined)
        && value.RestoreModes.Distinct().Count() == value.RestoreModes.Length
        && value.RestoreModes.All(Enum.IsDefined)
        && value.RestoreRecoveryFloorsSupported == !value.RestoreModes.IsDefaultOrEmpty);

    private static bool IsUnsupported(BaseSemanticActivationCapability value) => !value.SameTransactionModuleMutationSupported
        && !value.MaintenanceSupported
        && !value.RestoreRecoveryFloorsSupported && value.MaximumDefinitions == 0 && value.MaximumKeyBytes == 0
        && value.MaximumLiveSlots == 0 && value.MaximumRetiredSlots == 0 && value.MaximumAbsenceMarkers == 0
        && value.MaximumOperationsPerTransaction == 0 && value.MaximumScopeDirectoryReads == 0
        && value.MaximumSlotReads == 0 && value.MaximumActivationReads == 0 && value.MaximumReadIntervals == 0
        && value.MaximumIndexOperations == 0 && value.MaximumActivationBytes == 0 && value.MaximumScopeDirectoryBytes == 0
        && value.MaximumEvidenceBytes == 0 && value.MaximumReceiptBytes == 0 && value.MaximumTransientBytes == 0
        && value.MaximumMaintenancePageSize == 0 && value.BackupModes.IsDefaultOrEmpty && value.RestoreModes.IsDefaultOrEmpty
        && value.Deadlines.AcquisitionTimeout == TimeSpan.Zero && value.Deadlines.TransactionTimeout == TimeSpan.Zero
        && value.Deadlines.CommitObservationTimeout == TimeSpan.Zero && value.Deadlines.ReceiptResolutionTimeout == TimeSpan.Zero
        && value.Deadlines.MaintenanceTimeout == TimeSpan.Zero && value.Deadlines.QuarantineRetentionTimeout == TimeSpan.Zero;

    private static bool ValidDeadlines(BaseSemanticActivationDeadlineCapability value) =>
        value.AcquisitionTimeout > TimeSpan.Zero && value.AcquisitionTimeout <= TimeSpan.FromSeconds(5)
        && value.TransactionTimeout > TimeSpan.Zero && value.TransactionTimeout <= TimeSpan.FromSeconds(30)
        && value.CommitObservationTimeout > TimeSpan.Zero && value.CommitObservationTimeout <= TimeSpan.FromSeconds(30)
        && value.ReceiptResolutionTimeout > TimeSpan.Zero && value.ReceiptResolutionTimeout <= TimeSpan.FromSeconds(30)
        && value.MaintenanceTimeout > TimeSpan.Zero && value.MaintenanceTimeout <= TimeSpan.FromMinutes(5)
        && value.QuarantineRetentionTimeout > TimeSpan.Zero && value.QuarantineRetentionTimeout <= TimeSpan.FromMinutes(5);
}
