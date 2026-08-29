using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Contains durable ordered authority for definition-bound activation-instance receipts.</summary>
public sealed record BaseActivationInstanceReceiptChainState
{
    /// <summary>Gets the closed chain-state format version.</summary>
    public required int FormatVersion { get; init; }
    /// <summary>Gets the last committed, gap-free receipt sequence.</summary>
    public required long CurrentSequence { get; init; }
    /// <summary>Gets the ordered checksum through <see cref="CurrentSequence"/>.</summary>
    public required ImmutableArray<byte> OrderedChecksum { get; init; }
    /// <summary>Gets the monotonic chain-maintenance generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the canonical state checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Creates and validates canonical L76 activation-instance receipt-chain authority.</summary>
public static class BaseActivationInstanceReceiptChainContract
{
    private static readonly byte[] Zero = SHA256.HashData(Encoding.UTF8.GetBytes("base.activation.receiptChain.v1\0"));

    /// <summary>Gets a fresh copy of the sequence-zero ordered checksum.</summary>
    public static ImmutableArray<byte> ZeroOrderedChecksum => Zero.ToArray().ToImmutableArray();

    /// <summary>Creates one deeply owned canonical chain state.</summary>
    public static BaseActivationInstanceReceiptChainState Create(
        long currentSequence,
        ReadOnlySpan<byte> orderedChecksum,
        long generation)
    {
        if (currentSequence < 0 || generation < 0 || orderedChecksum.Length != 32)
            throw new ArgumentOutOfRangeException(nameof(currentSequence));
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("base.activation.receiptChainState.v1\0"));
        Span<byte> fields = stackalloc byte[20];
        BinaryPrimitives.WriteInt32BigEndian(fields, 1);
        BinaryPrimitives.WriteInt64BigEndian(fields[4..], currentSequence);
        BinaryPrimitives.WriteInt64BigEndian(fields[12..], generation);
        hash.AppendData(fields);
        hash.AppendData(orderedChecksum);
        return new BaseActivationInstanceReceiptChainState
        {
            FormatVersion = 1,
            CurrentSequence = currentSequence,
            OrderedChecksum = orderedChecksum.ToArray().ToImmutableArray(),
            Generation = generation,
            Checksum = hash.GetHashAndReset().ToImmutableArray(),
        };
    }

    /// <summary>Computes the ordered checksum contributed by one activation-instance receipt.</summary>
    public static ImmutableArray<byte> Append(
        long sequence,
        ReadOnlySpan<byte> priorOrderedChecksum,
        ReadOnlySpan<byte> receiptAuthorityChecksum,
        string receiptKey)
    {
        if (sequence <= 0 || priorOrderedChecksum.Length != 32 || receiptAuthorityChecksum.Length != 32)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptKey);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes("base.activation.receiptChain.v1\0"));
        Span<byte> sequenceBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(sequenceBytes, sequence);
        hash.AppendData(sequenceBytes);
        AppendFramed(hash, priorOrderedChecksum);
        AppendFramed(hash, receiptAuthorityChecksum);
        AppendFramed(hash, Encoding.UTF8.GetBytes(receiptKey));
        return hash.GetHashAndReset().ToImmutableArray();
    }

    /// <summary>Computes the purpose-bound authority checksum for one complete activation-instance receipt envelope.</summary>
    public static ImmutableArray<byte> ReceiptAuthorityChecksum(
        string receiptKey,
        string operationKind,
        string activationId,
        BaseActivationDefinitionKey definition,
        BaseActivationReceiptRetentionPolicy retention,
        ReadOnlySpan<byte> fingerprint,
        ReadOnlySpan<byte> resultChecksum,
        long committedAt,
        long duplicateResolveUntil,
        long receiptSequence,
        ReadOnlySpan<byte> priorOrderedChecksum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(retention);
        if (definition.Version <= 0 || definition.Checksum.Length != 32
            || retention.FormatVersion != 1
            || retention.DuplicateResolutionLifetime.Ticks % TimeSpan.TicksPerMillisecond != 0
            || retention.DuplicateResolutionLifetime < TimeSpan.FromHours(1)
            || retention.DuplicateResolutionLifetime > TimeSpan.FromDays(90)
            || !Enum.IsDefined(retention.ProtectedBackupCoverage)
            || fingerprint.Length != 32 || resultChecksum.Length != 32
            || committedAt < 0 || duplicateResolveUntil <= committedAt || receiptSequence <= 0
            || priorOrderedChecksum.Length != 32
            || duplicateResolveUntil - committedAt
                != retention.DuplicateResolutionLifetime.Ticks / TimeSpan.TicksPerMillisecond)
            throw new ArgumentException("The activation-instance receipt envelope is not canonical.");

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.activation.instanceReceipt.v1\0"u8);
        AppendFramed(hash, Encoding.UTF8.GetBytes(receiptKey));
        AppendFramed(hash, Encoding.UTF8.GetBytes(operationKind));
        AppendFramed(hash, Encoding.UTF8.GetBytes(activationId));
        AppendFramed(hash, Encoding.UTF8.GetBytes(definition.Id));
        Span<byte> numbers = stackalloc byte[52];
        BinaryPrimitives.WriteInt32BigEndian(numbers, definition.Version);
        BinaryPrimitives.WriteInt32BigEndian(numbers[4..], retention.FormatVersion);
        BinaryPrimitives.WriteInt64BigEndian(numbers[8..], retention.DuplicateResolutionLifetime.Ticks / TimeSpan.TicksPerMillisecond);
        BinaryPrimitives.WriteInt32BigEndian(numbers[16..], (int)retention.ProtectedBackupCoverage);
        BinaryPrimitives.WriteInt64BigEndian(numbers[20..], committedAt);
        BinaryPrimitives.WriteInt64BigEndian(numbers[28..], duplicateResolveUntil);
        BinaryPrimitives.WriteInt64BigEndian(numbers[36..], receiptSequence);
        BinaryPrimitives.WriteInt64BigEndian(numbers[44..], 0);
        hash.AppendData(numbers);
        AppendFramed(hash, definition.Checksum.AsSpan());
        AppendFramed(hash, fingerprint);
        AppendFramed(hash, resultChecksum);
        AppendFramed(hash, priorOrderedChecksum);
        return hash.GetHashAndReset().ToImmutableArray();
    }

    /// <summary>Returns whether the supplied state is canonical.</summary>
    public static bool IsValid(BaseActivationInstanceReceiptChainState? value)
    {
        if (value is null || value.FormatVersion != 1 || value.OrderedChecksum.Length != 32 || value.Checksum.Length != 32)
            return false;
        try
        {
            BaseActivationInstanceReceiptChainState canonical = Create(value.CurrentSequence, value.OrderedChecksum.AsSpan(), value.Generation);
            return CryptographicOperations.FixedTimeEquals(canonical.Checksum.AsSpan(), value.Checksum.AsSpan())
                && (value.CurrentSequence != 0 || CryptographicOperations.FixedTimeEquals(value.OrderedChecksum.AsSpan(), Zero));
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static void AppendFramed(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}

/// <summary>Contains authenticated publication authority for one backup-covered activation receipt-chain prefix.</summary>
public sealed record BaseActivationBackupCoverageCheckpoint
{
    /// <summary>Gets the closed checkpoint representation version.</summary>
    public required int FormatVersion { get; init; }
    /// <summary>Gets the canonical artifact identity.</summary>
    public required string ArtifactId { get; init; }
    /// <summary>Gets the SHA-256 digest of the authenticated artifact.</summary>
    public required ImmutableArray<byte> ArtifactSha256 { get; init; }
    /// <summary>Gets the owning application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the configured logical store identity.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the physical store-instance identity.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the restore epoch under which the checkpoint was published.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the covered activation-instance receipt sequence.</summary>
    public required long ReceiptSequence { get; init; }
    /// <summary>Gets the ordered receipt-chain checksum through <see cref="ReceiptSequence"/>.</summary>
    public required ImmutableArray<byte> ReceiptOrderedChecksum { get; init; }
    /// <summary>Gets the positive monotonic checkpoint generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the provider-accepted publication time in UTC Unix milliseconds.</summary>
    public required long CommittedAt { get; init; }
    /// <summary>Gets the canonical checkpoint checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Creates and validates canonical L76 backup-coverage checkpoints.</summary>
public static class BaseActivationBackupCoverageCheckpointContract
{
    /// <summary>Creates one deeply owned checkpoint with its canonical checksum.</summary>
    public static BaseActivationBackupCoverageCheckpoint Create(
        string artifactId,
        ReadOnlySpan<byte> artifactSha256,
        string applicationId,
        string logicalStoreId,
        string storeInstanceId,
        long restoreEpoch,
        long receiptSequence,
        ReadOnlySpan<byte> receiptOrderedChecksum,
        long generation,
        long committedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalStoreId);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeInstanceId);
        if (artifactSha256.Length != 32 || receiptOrderedChecksum.Length != 32
            || restoreEpoch < 0 || receiptSequence < 0 || generation <= 0 || committedAt < 0)
            throw new ArgumentException("The activation backup-coverage checkpoint is not canonical.");
        BaseActivationBackupCoverageCheckpoint checkpoint = new()
        {
            FormatVersion = 1,
            ArtifactId = artifactId,
            ArtifactSha256 = artifactSha256.ToArray().ToImmutableArray(),
            ApplicationId = applicationId,
            LogicalStoreId = logicalStoreId,
            StoreInstanceId = storeInstanceId,
            RestoreEpoch = restoreEpoch,
            ReceiptSequence = receiptSequence,
            ReceiptOrderedChecksum = receiptOrderedChecksum.ToArray().ToImmutableArray(),
            Generation = generation,
            CommittedAt = committedAt,
            Checksum = [],
        };
        return checkpoint with { Checksum = Checksum(checkpoint) };
    }

    /// <summary>Returns whether the checkpoint has canonical shape and checksum.</summary>
    public static bool IsValid(BaseActivationBackupCoverageCheckpoint? value)
    {
        if (value is null || value.FormatVersion != 1 || value.ArtifactSha256.Length != 32
            || value.ReceiptOrderedChecksum.Length != 32 || value.Checksum.Length != 32)
            return false;
        try
        {
            BaseActivationBackupCoverageCheckpoint canonical = Create(
                value.ArtifactId, value.ArtifactSha256.AsSpan(), value.ApplicationId,
                value.LogicalStoreId, value.StoreInstanceId, value.RestoreEpoch,
                value.ReceiptSequence, value.ReceiptOrderedChecksum.AsSpan(), value.Generation,
                value.CommittedAt);
            return CryptographicOperations.FixedTimeEquals(canonical.Checksum.AsSpan(), value.Checksum.AsSpan());
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ImmutableArray<byte> Checksum(BaseActivationBackupCoverageCheckpoint value)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.activation.backupCoverageCheckpoint.v1\0"u8);
        Append(hash, Encoding.UTF8.GetBytes(value.ArtifactId));
        Append(hash, value.ArtifactSha256.AsSpan());
        Append(hash, Encoding.UTF8.GetBytes(value.ApplicationId));
        Append(hash, Encoding.UTF8.GetBytes(value.LogicalStoreId));
        Append(hash, Encoding.UTF8.GetBytes(value.StoreInstanceId));
        Span<byte> numbers = stackalloc byte[36];
        BinaryPrimitives.WriteInt32BigEndian(numbers, value.FormatVersion);
        BinaryPrimitives.WriteInt64BigEndian(numbers[4..], value.RestoreEpoch);
        BinaryPrimitives.WriteInt64BigEndian(numbers[12..], value.ReceiptSequence);
        BinaryPrimitives.WriteInt64BigEndian(numbers[20..], value.Generation);
        BinaryPrimitives.WriteInt64BigEndian(numbers[28..], value.CommittedAt);
        hash.AppendData(numbers);
        Append(hash, value.ReceiptOrderedChecksum.AsSpan());
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}

/// <summary>Creates canonical authority checksums for scheduler, executor, migration, and maintenance receipts.</summary>
public static class BaseActivationControlReceiptContract
{
    /// <summary>Computes the purpose-bound authority checksum for one control receipt.</summary>
    public static ImmutableArray<byte> AuthorityChecksum(
        string receiptKey,
        string operationKind,
        ReadOnlySpan<byte> fingerprint,
        ReadOnlySpan<byte> resultChecksum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        if (fingerprint.Length != 32 || resultChecksum.Length != 32)
            throw new ArgumentException("The activation control receipt is not canonical.");
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.activation.controlReceipt.v1\0"u8);
        Append(hash, Encoding.UTF8.GetBytes(receiptKey));
        Append(hash, Encoding.UTF8.GetBytes(operationKind));
        Append(hash, fingerprint);
        Append(hash, resultChecksum);
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}

/// <summary>Preserves the ordered-chain link of one compacted activation-instance receipt.</summary>
public sealed record BaseActivationCompactedReceiptFact
{
    /// <summary>Gets the closed fact format version.</summary>
    public required int FormatVersion { get; init; }
    /// <summary>Gets the original gap-free receipt sequence.</summary>
    public required long ReceiptSequence { get; init; }
    /// <summary>Gets the original durable receipt key.</summary>
    public required string ReceiptKey { get; init; }
    /// <summary>Gets the original purpose-bound receipt authority checksum.</summary>
    public required ImmutableArray<byte> ReceiptAuthorityChecksum { get; init; }
    /// <summary>Gets the chain checksum immediately before the original receipt.</summary>
    public required ImmutableArray<byte> PriorOrderedChecksum { get; init; }
    /// <summary>Gets the chain checksum through the original receipt.</summary>
    public required ImmutableArray<byte> OrderedChecksum { get; init; }
    /// <summary>Gets the identified compaction receipt that authorized payload deletion.</summary>
    public required string CompactionReceiptKey { get; init; }
    /// <summary>Gets the canonical compact-fact checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Creates and validates compact retained activation receipt-chain authority.</summary>
public static class BaseActivationCompactedReceiptFactContract
{
    /// <summary>Creates one deeply owned canonical compact fact.</summary>
    public static BaseActivationCompactedReceiptFact Create(
        long receiptSequence,
        string receiptKey,
        ReadOnlySpan<byte> receiptAuthorityChecksum,
        ReadOnlySpan<byte> priorOrderedChecksum,
        ReadOnlySpan<byte> orderedChecksum,
        string compactionReceiptKey)
    {
        if (receiptSequence < 1 || string.IsNullOrWhiteSpace(receiptKey)
            || string.IsNullOrWhiteSpace(compactionReceiptKey)
            || receiptAuthorityChecksum.Length != 32 || priorOrderedChecksum.Length != 32
            || orderedChecksum.Length != 32)
            throw new ArgumentException("The compacted activation receipt fact is not canonical.");
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.activation.compactedReceiptFact.v1\0"u8);
        Span<byte> sequence = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(sequence, receiptSequence); hash.AppendData(sequence);
        Append(hash, Encoding.UTF8.GetBytes(receiptKey)); hash.AppendData(receiptAuthorityChecksum);
        hash.AppendData(priorOrderedChecksum); hash.AppendData(orderedChecksum);
        Append(hash, Encoding.UTF8.GetBytes(compactionReceiptKey));
        return new BaseActivationCompactedReceiptFact
        {
            FormatVersion = 1, ReceiptSequence = receiptSequence, ReceiptKey = new string(receiptKey.AsSpan()),
            ReceiptAuthorityChecksum = receiptAuthorityChecksum.ToArray().ToImmutableArray(),
            PriorOrderedChecksum = priorOrderedChecksum.ToArray().ToImmutableArray(),
            OrderedChecksum = orderedChecksum.ToArray().ToImmutableArray(),
            CompactionReceiptKey = new string(compactionReceiptKey.AsSpan()),
            Checksum = hash.GetHashAndReset().ToImmutableArray(),
        };
    }

    /// <summary>Returns whether one compact fact is canonical and internally consistent.</summary>
    public static bool IsValid(BaseActivationCompactedReceiptFact? value)
    {
        if (value is null || value.FormatVersion != 1 || value.Checksum.Length != 32) return false;
        try
        {
            BaseActivationCompactedReceiptFact expected = Create(
                value.ReceiptSequence, value.ReceiptKey, value.ReceiptAuthorityChecksum.AsSpan(),
                value.PriorOrderedChecksum.AsSpan(), value.OrderedChecksum.AsSpan(), value.CompactionReceiptKey);
            return CryptographicOperations.FixedTimeEquals(expected.Checksum.AsSpan(), value.Checksum.AsSpan());
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length); hash.AppendData(value);
    }
}

/// <summary>Contains durable provider authority for reserved and retained yield-receipt slots.</summary>
public sealed record BaseActivationYieldReservationState
{
    /// <summary>Gets the closed reservation-state format version.</summary>
    public required int FormatVersion { get; init; }
    /// <summary>Gets the monotonic reservation-state generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the immutable store-lifetime maximum slot authority.</summary>
    public required long MaximumSlots { get; init; }
    /// <summary>Gets the number of reserved but unused slots.</summary>
    public required long ReservedUnusedSlots { get; init; }
    /// <summary>Gets the number of retained used slots.</summary>
    public required long RetainedUsedSlots { get; init; }
    /// <summary>Gets the canonical authority checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Creates and validates canonical durable-yield reservation authority.</summary>
public static class BaseActivationYieldReservationContract
{
    /// <summary>Creates one deeply owned canonical reservation snapshot.</summary>
    /// <param name="generation">The monotonic state generation.</param>
    /// <param name="maximumSlots">The immutable store-lifetime maximum.</param>
    /// <param name="reservedUnusedSlots">The reserved-unused counter.</param>
    /// <param name="retainedUsedSlots">The retained-used counter.</param>
    /// <returns>The canonical snapshot.</returns>
    public static BaseActivationYieldReservationState Create(
        long generation,
        long maximumSlots,
        long reservedUnusedSlots,
        long retainedUsedSlots)
    {
        if (generation < 0 || maximumSlots < 0 || reservedUnusedSlots < 0 || retainedUsedSlots < 0
            || checked(reservedUnusedSlots + retainedUsedSlots) > maximumSlots)
            throw new ArgumentOutOfRangeException(nameof(reservedUnusedSlots));
        Span<byte> fields = stackalloc byte[36];
        BinaryPrimitives.WriteInt32BigEndian(fields, 1);
        BinaryPrimitives.WriteInt64BigEndian(fields[4..], generation);
        BinaryPrimitives.WriteInt64BigEndian(fields[12..], maximumSlots);
        BinaryPrimitives.WriteInt64BigEndian(fields[20..], reservedUnusedSlots);
        BinaryPrimitives.WriteInt64BigEndian(fields[28..], retainedUsedSlots);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes("base.activation.yieldReservationState.v1\0"));
        hash.AppendData(fields);
        return new BaseActivationYieldReservationState
        {
            FormatVersion = 1,
            Generation = generation,
            MaximumSlots = maximumSlots,
            ReservedUnusedSlots = reservedUnusedSlots,
            RetainedUsedSlots = retainedUsedSlots,
            Checksum = hash.GetHashAndReset().ToImmutableArray(),
        };
    }

    /// <summary>Returns whether a snapshot is canonical and valid.</summary>
    /// <param name="value">The snapshot to validate.</param>
    /// <returns><see langword="true"/> only for canonical authority.</returns>
    public static bool IsValid(BaseActivationYieldReservationState? value)
    {
        if (value is null || value.FormatVersion != 1 || value.Checksum.Length != 32) return false;
        try
        {
            BaseActivationYieldReservationState canonical = Create(
                value.Generation, value.MaximumSlots, value.ReservedUnusedSlots, value.RetainedUsedSlots);
            return CryptographicOperations.FixedTimeEquals(canonical.Checksum.AsSpan(), value.Checksum.AsSpan());
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}

/// <summary>Classifies how an activation provider invalidates a due observation.</summary>
public enum BaseDueInvalidationClass
{
    /// <summary>The provider supplies native finite-token invalidation.</summary>
    Native = 0,
    /// <summary>The provider supplies certified bounded polling.</summary>
    BoundedPolling = 1,
}

/// <summary>Classifies one provider-supported durable schedule expression.</summary>
public enum BaseScheduleKind
{
    /// <summary>One exact instant.</summary>
    Once,
    /// <summary>One fixed UTC interval.</summary>
    Interval,
    /// <summary>One six-field calendar cron expression.</summary>
    Cron,
    /// <summary>One closed calendar recurrence.</summary>
    Calendar,
}

/// <summary>Classifies the result of waiting on one finite due observation.</summary>
public enum BaseDueWaitOutcome
{
    /// <summary>The observed due authority changed.</summary>
    Changed = 0,
    /// <summary>The requested finite deadline elapsed.</summary>
    Deadline = 1,
    /// <summary>The provider is shutting down.</summary>
    Shutdown = 2,
    /// <summary>The observation token is no longer valid.</summary>
    TokenInvalid = 3,
}

/// <summary>Provides protected authority for one exact activation scope seek.</summary>
public sealed record BaseOwnedScopeSeekAuthority
{
    /// <summary>Gets the semantic scope kind.</summary>
    public required BaseSubjectScopeKind Kind { get; init; }
    /// <summary>Gets the purpose-bound protected index digest.</summary>
    public required ImmutableArray<byte> ProtectedIndexDigest { get; init; }
}

/// <summary>Contains a Runtime-issued, provider-verifiable accepted-time observation.</summary>
public sealed class BaseAcceptedTimeReceipt
{
    internal BaseAcceptedTimeReceipt(
        string applicationId,
        long clockGeneration,
        long capturedUtc,
        long monotonicTimestamp,
        long captureSequence,
        long maximumForwardSkewMilliseconds,
        byte[] checksum)
    {
        ApplicationId = applicationId;
        ClockGeneration = clockGeneration;
        CapturedUtc = capturedUtc;
        MonotonicTimestamp = monotonicTimestamp;
        CaptureSequence = captureSequence;
        MaximumForwardSkewMilliseconds = maximumForwardSkewMilliseconds;
        Checksum = checksum.ToArray();
    }

    /// <summary>Gets the installed application identity.</summary>
    public string ApplicationId { get; }
    /// <summary>Gets the positive installed clock generation.</summary>
    public long ClockGeneration { get; }
    /// <summary>Gets accepted UTC time as Unix milliseconds.</summary>
    public long CapturedUtc { get; }
    /// <summary>Gets the process-monotonic timestamp used for validation.</summary>
    public long MonotonicTimestamp { get; }
    /// <summary>Gets the positive capture sequence.</summary>
    public long CaptureSequence { get; }
    /// <summary>Gets the maximum accepted forward skew in milliseconds.</summary>
    public long MaximumForwardSkewMilliseconds { get; }
    /// <summary>Gets the canonical receipt checksum.</summary>
    public ReadOnlyMemory<byte> Checksum { get; }
}

/// <summary>Defines the complete effective safety envelope for one activation-provider operation.</summary>
public sealed record BaseActivationExecutionLimits
{
    /// <summary>Gets the maximum candidates a due seek may inspect.</summary>
    public required int MaximumCandidates { get; init; }
    /// <summary>Gets the maximum canonical input bytes retained.</summary>
    public required long MaximumInputBytes { get; init; }
    /// <summary>Gets the maximum canonical result bytes retained.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the maximum evidence bytes returned.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the maximum aggregate transient bytes retained.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the maximum read intervals returned.</summary>
    public required int MaximumReadIntervals { get; init; }
    /// <summary>Gets the maximum index operations performed.</summary>
    public required int MaximumIndexOperations { get; init; }
    /// <summary>Gets the acquisition deadline.</summary>
    public required TimeSpan AcquisitionTimeout { get; init; }
    /// <summary>Gets the transaction deadline.</summary>
    public required TimeSpan TransactionTimeout { get; init; }
    /// <summary>Gets the commit-observation deadline.</summary>
    public required TimeSpan CommitObservationTimeout { get; init; }
    /// <summary>Gets the receipt-resolution deadline.</summary>
    public required TimeSpan ReceiptResolutionTimeout { get; init; }
}

/// <summary>Reports canonical provider work for one activation operation.</summary>
public sealed record BaseActivationAccounting
{
    /// <summary>Gets the number of candidate rows inspected.</summary>
    public required int Candidates { get; init; }
    /// <summary>Gets the number of comparisons performed.</summary>
    public required int Comparisons { get; init; }
    /// <summary>Gets the number of index operations performed.</summary>
    public required int IndexOperations { get; init; }
    /// <summary>Gets the number of returned read intervals.</summary>
    public required int ReadIntervals { get; init; }
    /// <summary>Gets the canonical evidence byte count.</summary>
    public required long EvidenceBytes { get; init; }
    /// <summary>Gets the aggregate transient byte count.</summary>
    public required long TransientBytes { get; init; }
}

/// <summary>Requests resolution of one identified durable activation receipt.</summary>
public sealed record BaseActivationReceiptResolutionRequest
{
    /// <summary>Gets the exact historical request identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets current trusted time used for claim-authority replay.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the exact effective provider limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains one checksum-validated, provider-neutral receipt resolution.</summary>
public sealed record BaseActivationReceiptResolution
{
    /// <summary>Gets the closed provider operation kind.</summary>
    public required string OperationKind { get; init; }
    /// <summary>Gets the exact stored request fingerprint.</summary>
    public required ImmutableArray<byte> Fingerprint { get; init; }
    /// <summary>Gets canonical source-generated result bytes after current claim resolution.</summary>
    public required ImmutableArray<byte> CanonicalResult { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
}

/// <summary>Contains the canonical total-order boundary for a due activation.</summary>
public sealed record BaseActivationDueBoundary
{
    /// <summary>Gets the effective aged priority.</summary>
    public required int EffectiveAgedPriority { get; init; }
    /// <summary>Gets the effective due instant as Unix milliseconds.</summary>
    public required long EffectiveDueAt { get; init; }
    /// <summary>Gets the optional schedule occurrence identity.</summary>
    public string? OccurrenceId { get; init; }
    /// <summary>Gets the stable activation identity.</summary>
    public required string ActivationId { get; init; }
}

/// <summary>Contains one opaque, finite due-observation token.</summary>
public sealed record BaseDueObservationToken
{
    /// <summary>Gets the purpose-bound authenticated token bytes.</summary>
    public required ImmutableArray<byte> Value { get; init; }
}

/// <summary>Requests an exact protected due observation.</summary>
public sealed record BaseActivationDueObservationRequest
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the installed worker module identity.</summary>
    public required string WorkerModuleId { get; init; }
    /// <summary>Gets the canonical authorized definition set.</summary>
    public required ImmutableArray<BaseActivationDefinitionKey> Definitions { get; init; }
    /// <summary>Gets the protected exact scope seek.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets the accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the maximum candidates to inspect.</summary>
    public required int MaximumCandidates { get; init; }
    /// <summary>Gets the optional exclusive continuation boundary.</summary>
    public BaseActivationDueBoundary? After { get; init; }
    /// <summary>Gets the effective operation limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains an authoritative finite due observation.</summary>
public sealed record BaseActivationDueObservation
{
    /// <summary>Gets the earliest matching due boundary, or null for an empty observation.</summary>
    public BaseActivationDueBoundary? Earliest { get; init; }
    /// <summary>Gets the finite observation token.</summary>
    public required BaseDueObservationToken Token { get; init; }
    /// <summary>Gets normalized covering read intervals.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> Intervals { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
}

/// <summary>Contains the result of waiting on a due observation.</summary>
public sealed record BaseDueWaitResult
{
    /// <summary>Gets the closed wait outcome.</summary>
    public required BaseDueWaitOutcome Outcome { get; init; }
}

/// <summary>Contains exact installed authority for one worker.</summary>
public sealed record BaseActivationWorkerAuthority
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the worker module identity.</summary>
    public required string ModuleId { get; init; }
    /// <summary>Gets the worker identity.</summary>
    public required string WorkerIdentity { get; init; }
    /// <summary>Gets the authorized definitions in canonical order.</summary>
    public required ImmutableArray<BaseActivationDefinitionKey> Definitions { get; init; }
    /// <summary>Gets the protected exact scope.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets the canonical authority checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains one durable activation-attempt observation.</summary>
public sealed record BaseActivationAttemptEvidence
{
    /// <summary>Gets the stable attempt identity.</summary>
    public required string AttemptId { get; init; }
    /// <summary>Gets the positive attempt number.</summary>
    public required int AttemptNumber { get; init; }
    /// <summary>Gets the accepted start instant as Unix milliseconds.</summary>
    public required long StartedAt { get; init; }
    /// <summary>Gets the canonical evidence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Requests one atomic due seek and claim.</summary>
public sealed record BaseActivationClaimRequest
{
    /// <summary>Gets the current due-observation token.</summary>
    public required BaseDueObservationToken Observation { get; init; }
    /// <summary>Gets the installed worker authority.</summary>
    public required BaseActivationWorkerAuthority Worker { get; init; }
    /// <summary>Gets accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the requested lease duration in milliseconds.</summary>
    public required long LeaseMilliseconds { get; init; }
    /// <summary>Gets the identified claim request.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective operation limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Represents the closed result of an atomic claim operation.</summary>
[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimedResult), "claimed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimEmptyResult), "empty")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationObservationChangedResult), "observationChanged")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimCapacityResult), "capacity")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationRecoveredClaimResult), "recovered")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimExpiredResult), "expired")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimSupersededResult), "superseded")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimCancelledResult), "cancelled")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimTerminalResult), "terminal")]
public abstract record BaseActivationClaimResult;

/// <summary>Contains a newly committed activation claim.</summary>
public sealed record BaseActivationClaimedResult(
    BaseActivationPayload Payload,
    BaseActivationClaimAuthority Claim,
    BaseActivationLeaseObservation Lease,
    BaseActivationAttemptEvidence Attempt,
    ImmutableArray<BaseAtomicReadIntervalEvidence> Intervals,
    BaseActivationAccounting Accounting) : BaseActivationClaimResult;

/// <summary>Reports that no eligible activation exists under a replacement observation.</summary>
public sealed record BaseActivationClaimEmptyResult(BaseDueObservationToken Replacement) : BaseActivationClaimResult;

/// <summary>Reports that the supplied observation changed before claiming.</summary>
public sealed record BaseActivationObservationChangedResult(BaseDueObservationToken Replacement) : BaseActivationClaimResult;

/// <summary>Reports bounded provider capacity exhaustion.</summary>
public sealed record BaseActivationClaimCapacityResult(TimeSpan RetryAfter) : BaseActivationClaimResult;

/// <summary>Reports one recovered expired claim; callers must observe again.</summary>
public sealed record BaseActivationRecoveredClaimResult(string ActivationId, long ResultingGeneration) : BaseActivationClaimResult;

/// <summary>Reports that a historically committed claim is no longer leased.</summary>
public sealed record BaseActivationClaimExpiredResult(string ActivationId) : BaseActivationClaimResult;

/// <summary>Reports that a later claim epoch superseded the historical claim.</summary>
public sealed record BaseActivationClaimSupersededResult(string ActivationId) : BaseActivationClaimResult;

/// <summary>Reports that the activation was cancelled after the historical claim.</summary>
public sealed record BaseActivationClaimCancelledResult(string ActivationId) : BaseActivationClaimResult;

/// <summary>Reports that the activation is already terminal.</summary>
public sealed record BaseActivationClaimTerminalResult(string ActivationId, BaseActivationState State) : BaseActivationClaimResult;

/// <summary>Requests renewal of one current activation lease.</summary>
public sealed record BaseActivationRenewRequest
{
    /// <summary>Gets stable claim authority.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets the expected positive lease revision.</summary>
    public required long ExpectedLeaseRevision { get; init; }
    /// <summary>Gets accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the requested lease extension in milliseconds.</summary>
    public required long ExtensionMilliseconds { get; init; }
    /// <summary>Gets the identified renewal request.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective operation limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains a committed lease renewal.</summary>
public sealed record BaseActivationRenewResult
{
    /// <summary>Gets the byte-identical stable claim authority.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets the replacement lease observation.</summary>
    public required BaseActivationLeaseObservation Lease { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets duplicate-resolution disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Classifies a failed activation attempt.</summary>
public enum BaseActivationFailureDisposition
{
    /// <summary>Schedule another deterministic retry.</summary>
    Retry = 0,
    /// <summary>Terminalize the activation as exhausted.</summary>
    Exhaust = 1,
}

/// <summary>Classifies cancellation propagation.</summary>
public enum BaseCancellationPropagation
{
    /// <summary>Cancel only the selected activation.</summary>
    None = 0,
    /// <summary>Cancel all currently unstarted descendants in bounded maintenance.</summary>
    Descendants = 1,
}

/// <summary>Base contract for one identified activation state transition.</summary>
public abstract record BaseActivationTransitionRequest
{
    /// <summary>Gets the activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the identified transition request.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets effective operation limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Requests successful completion by the current claim.</summary>
public sealed record BaseActivationCompleteRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets current stable claim authority.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets canonical result bytes.</summary>
    public required ImmutableArray<byte> CanonicalResult { get; init; }
    /// <summary>Gets the canonical result checksum.</summary>
    public required ImmutableArray<byte> ResultChecksum { get; init; }
}

/// <summary>Requests failed-attempt handling by the current claim.</summary>
public sealed record BaseActivationFailRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets current stable claim authority.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets the stable safe failure code.</summary>
    public required string StableFailureCode { get; init; }
    /// <summary>Gets retry or exhaustion disposition.</summary>
    public required BaseActivationFailureDisposition Disposition { get; init; }
    /// <summary>Gets the exact Runtime-computed retry due instant, required only for retry.</summary>
    public long? RetryDueAt { get; init; }
}

/// <summary>Classifies one committed durable-yield transition.</summary>
public enum BaseActivationYieldDisposition
{
    /// <summary>The activation is waiting to resume.</summary>
    Yielded = 0,
    /// <summary>The activation exhausted its immutable yield ceiling.</summary>
    LimitExceeded = 1,
}

/// <summary>Requests claim-fenced durable resumption of the same activation.</summary>
public sealed record BaseActivationYieldRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the current stable claim authority.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets the authored optional resume instant.</summary>
    public DateTimeOffset? RequestedResumeAt { get; init; }
    /// <summary>Gets the Runtime/provider-derived effective due instant as Unix milliseconds.</summary>
    public required long EffectiveDueAt { get; init; }
    /// <summary>Gets the deeply owned progress fingerprint.</summary>
    public required ImmutableArray<byte> ProgressFingerprint { get; init; }
    /// <summary>Gets the expected current durable-yield count.</summary>
    public required long ExpectedYieldCount { get; init; }
    /// <summary>Gets the immutable maximum yields pinned to the activation.</summary>
    public required long MaximumYields { get; init; }
}

/// <summary>Requests cancellation using an exact control generation.</summary>
public sealed record BaseActivationCancelRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the expected activation generation.</summary>
    public required long ExpectedGeneration { get; init; }
    /// <summary>Gets cancellation propagation.</summary>
    public required BaseCancellationPropagation Propagation { get; init; }
}

/// <summary>Requests durable effect-start before one external side effect.</summary>
public sealed record BaseActivationBeginEffectRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the current activation claim.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets the complete current executor incarnation.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets the current executor heartbeat observation.</summary>
    public required BaseExecutorHeartbeatObservation ExecutorHeartbeat { get; init; }
    /// <summary>Gets the requested effect-heartbeat lifetime.</summary>
    public required long HeartbeatMilliseconds { get; init; }
}

/// <summary>Requests renewal of one started external-effect heartbeat.</summary>
public sealed record BaseActivationEffectHeartbeatRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets current effect authority.</summary>
    public required BaseEffectExecutionAuthority Effect { get; init; }
    /// <summary>Gets the expected effect-heartbeat revision.</summary>
    public required long ExpectedHeartbeatRevision { get; init; }
    /// <summary>Gets the requested heartbeat extension.</summary>
    public required long ExtensionMilliseconds { get; init; }
}

/// <summary>Requests successful terminalization of one started external effect.</summary>
public sealed record BaseActivationCompleteEffectRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets current effect authority.</summary>
    public required BaseEffectExecutionAuthority Effect { get; init; }
    /// <summary>Gets canonical result bytes.</summary>
    public required ImmutableArray<byte> CanonicalResult { get; init; }
    /// <summary>Gets the canonical result checksum.</summary>
    public required ImmutableArray<byte> ResultChecksum { get; init; }
}

/// <summary>Requests recovery of an expired effect whose external outcome is unknowable.</summary>
public sealed record BaseActivationRecoverEffectRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the exact effect authority expected to be abandoned.</summary>
    public required BaseEffectExecutionAuthority Effect { get; init; }
}

/// <summary>Classifies an operator-verified resolution of an ambiguous external effect.</summary>
public enum BaseEffectReconciliationDisposition
{
    /// <summary>The external effect is verified successful.</summary>
    Succeeded = 0,
    /// <summary>The external effect is verified failed and terminal.</summary>
    Exhausted = 1,
    /// <summary>The ambiguous activation is administratively disposed.</summary>
    Disposed = 2,
}

/// <summary>Requests identified operator reconciliation of one outcome-unknown effect.</summary>
public sealed record BaseActivationReconcileEffectRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the expected effect-start generation retained with ambiguity.</summary>
    public required long ExpectedEffectStartGeneration { get; init; }
    /// <summary>Gets the expected retained effect-authority checksum.</summary>
    public required ImmutableArray<byte> ExpectedEffectChecksum { get; init; }
    /// <summary>Gets the expected activation generation in the outcome-unknown state.</summary>
    public required long ExpectedGeneration { get; init; }
    /// <summary>Gets the selected terminal disposition.</summary>
    public required BaseEffectReconciliationDisposition Disposition { get; init; }
    /// <summary>Gets bounded canonical external verification evidence.</summary>
    public required ImmutableArray<byte> VerificationEvidence { get; init; }
    /// <summary>Gets the SHA-256 checksum of the verification evidence.</summary>
    public required ImmutableArray<byte> VerificationChecksum { get; init; }
}

/// <summary>Requests an identified operator retry of one exhausted activation.</summary>
public sealed record BaseActivationOperatorRetryRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the exact exhausted activation generation.</summary>
    public required long ExpectedGeneration { get; init; }
    /// <summary>Gets the Runtime-accepted retry due instant.</summary>
    public required long RetryDueAt { get; init; }
}

/// <summary>Requests identified disposal of retained terminal activation authority.</summary>
public sealed record BaseActivationDisposeRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the exact terminal activation generation.</summary>
    public required long ExpectedGeneration { get; init; }
}

/// <summary>Contains one committed activation transition.</summary>
public sealed record BaseActivationTransitionResult
{
    /// <summary>Gets the resulting state.</summary>
    public required BaseActivationState State { get; init; }
    /// <summary>Gets the resulting activation generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the resulting control checksum.</summary>
    public required ImmutableArray<byte> ControlChecksum { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets duplicate-resolution disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
    /// <summary>Gets current effect authority for effect-start and heartbeat transitions.</summary>
    public BaseEffectExecutionAuthority? Effect { get; init; }
    /// <summary>Gets canonical graph-owned result bytes when this transition commits a result.</summary>
    public ImmutableArray<byte> CanonicalResult { get; init; }
    /// <summary>Gets the resulting durable-yield count.</summary>
    public long YieldCount { get; init; }
    /// <summary>Gets the current execution-slice ordinal, or zero before first claim.</summary>
    public long ExecutionSliceOrdinal { get; init; }
    /// <summary>Gets the effective due instant committed by a yield transition.</summary>
    public long? EffectiveDueAt { get; init; }
    /// <summary>Gets the yield transition disposition when applicable.</summary>
    public BaseActivationYieldDisposition? YieldDisposition { get; init; }
    /// <summary>Gets the fixed yield terminal failure code only for limit exhaustion.</summary>
    public string? YieldTerminalFailureCode { get; init; }
}

/// <summary>Contains the deeply owned durable receipt for one committed activation yield.</summary>
public sealed record BaseActivationYieldReceipt
{
    /// <summary>Gets the exact installed definition authority.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the stable activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the generation captured before the transition.</summary>
    public required long PriorGeneration { get; init; }
    /// <summary>Gets the generation committed by the transition.</summary>
    public required long ResultingGeneration { get; init; }
    /// <summary>Gets the logical attempt number.</summary>
    public required int AttemptNumber { get; init; }
    /// <summary>Gets the execution-slice ordinal.</summary>
    public required long ExecutionSliceOrdinal { get; init; }
    /// <summary>Gets the accepted logical-attempt start.</summary>
    public required long AttemptStartedAt { get; init; }
    /// <summary>Gets the accepted current-slice start.</summary>
    public required long SliceStartedAt { get; init; }
    /// <summary>Gets the durable-yield count captured before the transition.</summary>
    public required long PriorYieldCount { get; init; }
    /// <summary>Gets the durable-yield count committed by the transition.</summary>
    public required long ResultingYieldCount { get; init; }
    /// <summary>Gets the effective due instant committed by the transition.</summary>
    public required long EffectiveDueAt { get; init; }
    /// <summary>Gets the opaque progress fingerprint.</summary>
    public required ImmutableArray<byte> ProgressFingerprint { get; init; }
    /// <summary>Gets the resulting durable activation state.</summary>
    public required BaseActivationState ResultingState { get; init; }
    /// <summary>Gets the closed durable-yield disposition.</summary>
    public required BaseActivationYieldDisposition Disposition { get; init; }
    /// <summary>Gets the fixed safe failure code only for yield-limit exhaustion.</summary>
    public string? FailureCode { get; init; }
    /// <summary>Gets the canonical resulting control checksum.</summary>
    public required ImmutableArray<byte> ControlChecksum { get; init; }
    /// <summary>Gets exact provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }

    internal BaseActivationTransitionResult ToTransitionResult(BaseMutationRequestDisposition requestDisposition) => new()
    {
        State = ResultingState,
        Generation = ResultingGeneration,
        ControlChecksum = ControlChecksum.ToArray().ToImmutableArray(),
        Accounting = Accounting with { },
        Disposition = requestDisposition,
        CanonicalResult = [],
        YieldCount = ResultingYieldCount,
        ExecutionSliceOrdinal = ExecutionSliceOrdinal,
        EffectiveDueAt = EffectiveDueAt,
        YieldDisposition = Disposition,
        YieldTerminalFailureCode = FailureCode,
    };
}

/// <summary>Requests registration of one durable worker-process incarnation.</summary>
public sealed record BaseExecutorRegistrationRequest
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the stable host identity.</summary>
    public required string HostId { get; init; }
    /// <summary>Gets the unique process-incarnation identity.</summary>
    public required string ProcessIncarnationId { get; init; }
    /// <summary>Gets the installed worker-definition-set checksum.</summary>
    public required ImmutableArray<byte> WorkerDefinitionSetChecksum { get; init; }
    /// <summary>Gets requested heartbeat lifetime in milliseconds.</summary>
    public required long RequestedHeartbeatMilliseconds { get; init; }
    /// <summary>Gets trusted accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the identified operation identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Requests renewal of one exact executor heartbeat.</summary>
public sealed record BaseExecutorHeartbeatRequest
{
    /// <summary>Gets stable executor authority.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets expected heartbeat revision.</summary>
    public required long ExpectedHeartbeatRevision { get; init; }
    /// <summary>Gets requested extension in milliseconds.</summary>
    public required long ExtensionMilliseconds { get; init; }
    /// <summary>Gets trusted accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets identified operation identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Requests retirement of one exact executor incarnation.</summary>
public sealed record BaseExecutorRetirementRequest
{
    /// <summary>Gets stable executor authority.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets expected heartbeat revision.</summary>
    public required long ExpectedHeartbeatRevision { get; init; }
    /// <summary>Gets trusted accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets identified operation identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains a newly registered executor incarnation.</summary>
public sealed record BaseExecutorRegistrationResult
{
    /// <summary>Gets stable incarnation authority.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets initial heartbeat observation.</summary>
    public required BaseExecutorHeartbeatObservation Heartbeat { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Contains a committed executor heartbeat replacement.</summary>
public sealed record BaseExecutorHeartbeatResult
{
    /// <summary>Gets byte-identical stable incarnation authority.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets replacement heartbeat observation.</summary>
    public required BaseExecutorHeartbeatObservation Heartbeat { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Contains committed terminal executor-registry evidence.</summary>
public sealed record BaseExecutorRetirementResult
{
    /// <summary>Gets stable retired incarnation authority.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets the terminal heartbeat revision.</summary>
    public required long HeartbeatRevision { get; init; }
    /// <summary>Gets terminal evidence checksum.</summary>
    public required ImmutableArray<byte> RetirementChecksum { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Identifies one exclusive activation-receipt compaction page boundary.</summary>
public sealed record BaseActivationReceiptCompactionCursor
{
    /// <summary>Gets the prior activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the prior receipt sequence.</summary>
    public required long ReceiptSequence { get; init; }
}

/// <summary>Classifies backup coverage required by one receipt compaction request.</summary>
public enum BaseActivationReceiptBackupFloorKind
{
    /// <summary>The installed receipt policy does not require protected-backup coverage.</summary>
    NotApplicable = 1,
    /// <summary>An exact authenticated backup checkpoint must cover each deleted receipt.</summary>
    Checkpoint = 2,
}

/// <summary>Contains the closed backup predicate for one receipt compaction page.</summary>
public sealed record BaseActivationReceiptBackupFloor
{
    /// <summary>Gets the closed backup-floor kind.</summary>
    public required BaseActivationReceiptBackupFloorKind Kind { get; init; }
    /// <summary>Gets the exact expected checkpoint only for <see cref="BaseActivationReceiptBackupFloorKind.Checkpoint"/>.</summary>
    public BaseActivationBackupCoverageCheckpoint? Checkpoint { get; init; }
}

/// <summary>Requests current provider-owned authority for one receipt-compaction attempt.</summary>
public sealed record BaseActivationReceiptCompactionAuthorityRequest
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the installed activation definition.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the exact immutable receipt-retention policy.</summary>
    public required BaseActivationReceiptRetentionPolicy ReceiptRetention { get; init; }
    /// <summary>Gets the protected scope seek.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets provider execution limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains current provider-owned authority for one receipt-compaction attempt.</summary>
public sealed record BaseActivationReceiptCompactionAuthority
{
    /// <summary>Gets the exact current yield-reservation authority.</summary>
    public required BaseActivationYieldReservationState Reservation { get; init; }
    /// <summary>Gets the exact current protected-backup predicate.</summary>
    public required BaseActivationReceiptBackupFloor BackupFloor { get; init; }
}

/// <summary>Requests one identified bounded activation-instance receipt compaction page.</summary>
public sealed record BaseActivationReceiptCompactionRequest
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the exact activation definition.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the exact immutable receipt-retention policy.</summary>
    public required BaseActivationReceiptRetentionPolicy ReceiptRetention { get; init; }
    /// <summary>Gets the protected scope seek.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets trusted accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the optional exclusive cursor.</summary>
    public BaseActivationReceiptCompactionCursor? After { get; init; }
    /// <summary>Gets the bounded number of candidates to examine.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the exact backup predicate.</summary>
    public required BaseActivationReceiptBackupFloor BackupFloor { get; init; }
    /// <summary>Gets the expected yield-reservation authority.</summary>
    public required BaseActivationYieldReservationState ExpectedReservation { get; init; }
    /// <summary>Gets provider limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
    /// <summary>Gets the identified request identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Contains one committed bounded activation-instance receipt compaction page.</summary>
public sealed record BaseActivationReceiptCompactionResult
{
    /// <summary>Gets the number of candidate receipts examined.</summary>
    public required int ExaminedCount { get; init; }
    /// <summary>Gets the number of receipt payloads deleted.</summary>
    public required int DeletedCount { get; init; }
    /// <summary>Gets the number of deleted receipt payloads that consumed retained yield slots.</summary>
    public required int DeletedYieldReceiptCount { get; init; }
    /// <summary>Gets the next exclusive cursor when more candidates may remain.</summary>
    public BaseActivationReceiptCompactionCursor? Next { get; init; }
    /// <summary>Gets chain authority before compaction.</summary>
    public required BaseActivationInstanceReceiptChainState PriorChain { get; init; }
    /// <summary>Gets chain authority after compaction.</summary>
    public required BaseActivationInstanceReceiptChainState ResultingChain { get; init; }
    /// <summary>Gets reservation authority before compaction.</summary>
    public required BaseActivationYieldReservationState PriorReservation { get; init; }
    /// <summary>Gets reservation authority after compaction.</summary>
    public required BaseActivationYieldReservationState ResultingReservation { get; init; }
    /// <summary>Gets the ordered digest of deleted receipt authorities.</summary>
    public required ImmutableArray<byte> DeletedAuthorityOrderedDigest { get; init; }
    /// <summary>Gets whether the ordered candidate scan completed.</summary>
    public required bool Completed { get; init; }
    /// <summary>Gets exact provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets committed or duplicate request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Describes one installed activation provider.</summary>
public sealed record BaseActivationProviderDescriptor
{
    /// <summary>Gets the stable provider identity.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets the provider implementation version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets the protocol version.</summary>
    public required int ProtocolVersion { get; init; }
    /// <summary>Gets provider capability.</summary>
    public required BaseActivationProviderCapability Capability { get; init; }
    /// <summary>Gets sorted native dependency receipts used during certification.</summary>
    public required ImmutableArray<string> NativeDependencyReceipts { get; init; }
    /// <summary>Gets the checksum of the provider certification contract.</summary>
    public required ImmutableArray<byte> CertificationContractChecksum { get; init; }
    /// <summary>Gets the checksum of the successful certification report.</summary>
    public required ImmutableArray<byte> CertificationReportChecksum { get; init; }
    /// <summary>Gets the purpose-bound deployment certification receipt.</summary>
    public required ImmutableArray<byte> CertificationReceipt { get; init; }
}

/// <summary>Requests the bounded durable definition dependencies required before application readiness.</summary>
public sealed record BaseActivationDependencyRequest
{
    /// <summary>Gets the exact application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the maximum distinct definitions the caller accepts.</summary>
    public required int MaximumDefinitions { get; init; }
    /// <summary>Gets the finite provider deadline.</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }
}

/// <summary>Describes one exact definition version referenced by durable provider authority.</summary>
public sealed record BaseActivationDefinitionDependency
{
    /// <summary>Gets the exact definition authority.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets whether at least one retained activation references the definition.</summary>
    public required bool ReferencedByActivation { get; init; }
    /// <summary>Gets whether at least one installed durable schedule references the definition.</summary>
    public required bool ReferencedBySchedule { get; init; }
}

/// <summary>Contains the complete bounded durable definition dependency set.</summary>
public sealed record BaseActivationDependencyResult
{
    /// <summary>Gets dependencies in definition ID, version, checksum order.</summary>
    public required ImmutableArray<BaseActivationDefinitionDependency> Dependencies { get; init; }
    /// <summary>Gets the provider generation at which the set was observed.</summary>
    public required long CapturedGeneration { get; init; }
    /// <summary>Gets exact provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
}

/// <summary>Declares certified activation-provider features and maxima.</summary>
public sealed record BaseActivationProviderCapability
{
    /// <summary>Gets whether atomic activation creation is supported.</summary>
    public required bool AtomicCreationSupported { get; init; }
    /// <summary>Gets whether transaction-bound selection targets are supported.</summary>
    public required bool SelectionTargetSupported { get; init; }
    /// <summary>Gets whether registered module-mutation targets are supported.</summary>
    public required bool ModuleTargetSupported { get; init; }
    /// <summary>Gets whether activation-guarded children are supported.</summary>
    public required bool GuardedChildrenSupported { get; init; }
    /// <summary>Gets whether claim-fenced durable yield is supported.</summary>
    public required bool DurableYieldSupported { get; init; }
    /// <summary>Gets whether restore fencing is supported.</summary>
    public required bool RestoreFencingSupported { get; init; }
    /// <summary>Gets due invalidation behavior.</summary>
    public required BaseDueInvalidationClass DueInvalidation { get; init; }
    /// <summary>Gets the closed supported schedule kinds.</summary>
    public required ImmutableArray<BaseScheduleKind> ScheduleKinds { get; init; }
    /// <summary>Gets the closed supported execution classes.</summary>
    public required ImmutableArray<BaseActivationExecutionClass> ExecutionClasses { get; init; }
    /// <summary>Gets maximum activations created in one transaction.</summary>
    public required int MaximumActivationsPerTransaction { get; init; }
    /// <summary>Gets maximum due candidates per seek.</summary>
    public required int MaximumDueCandidates { get; init; }
    /// <summary>Gets maximum normalized read intervals returned by one operation.</summary>
    public required int MaximumReadIntervals { get; init; }
    /// <summary>Gets maximum provider index operations charged to one operation.</summary>
    public required int MaximumIndexOperations { get; init; }
    /// <summary>Gets maximum canonical input bytes.</summary>
    public required long MaximumInputBytes { get; init; }
    /// <summary>Gets maximum canonical result bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets maximum evidence bytes.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets maximum transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets maximum receipt bytes.</summary>
    public required long MaximumReceiptBytes { get; init; }
    /// <summary>Gets maximum pending rows.</summary>
    public required int MaximumPendingRows { get; init; }
    /// <summary>Gets maximum claimed rows.</summary>
    public required int MaximumClaimedRows { get; init; }
    /// <summary>Gets maximum terminal rows.</summary>
    public required int MaximumTerminalRows { get; init; }
    /// <summary>Gets maximum attempts.</summary>
    public required int MaximumAttempts { get; init; }
    /// <summary>Gets the maximum durable yields pinned to one activation.</summary>
    public required long MaximumYieldsPerActivation { get; init; }
    /// <summary>Gets immutable store-lifetime reserved yield-receipt capacity.</summary>
    public required long MaximumReservedYieldReceiptSlots { get; init; }
    /// <summary>Gets maximum renewals per attempt.</summary>
    public required int MaximumRenewalsPerSlice { get; init; }
    /// <summary>Gets maximum guarded children per attempt.</summary>
    public required int MaximumChildrenPerSlice { get; init; }
    /// <summary>Gets maximum lineage depth.</summary>
    public required int MaximumLineageDepth { get; init; }
    /// <summary>Gets maximum occurrence page size.</summary>
    public required int MaximumOccurrencePage { get; init; }
    /// <summary>Gets the maximum priority boost produced by deterministic aging.</summary>
    public required int MaximumPriorityAgingBoost { get; init; }
    /// <summary>Gets the minimum elapsed interval represented by one priority-aging step.</summary>
    public required TimeSpan PriorityAgingInterval { get; init; }
    /// <summary>Gets the maximum lifetime of one finite due-observation token.</summary>
    public required TimeSpan ObservationTokenLifetime { get; init; }
    /// <summary>Gets maximum installed time-zone authority bytes.</summary>
    public required long MaximumTimeZoneBytes { get; init; }
    /// <summary>Gets maximum durable handler-definition dependencies checked during readiness.</summary>
    public required int MaximumHandlerDependencies { get; init; }
    /// <summary>Gets maximum acquisition deadline.</summary>
    public required TimeSpan AcquisitionDeadline { get; init; }
    /// <summary>Gets maximum transaction deadline.</summary>
    public required TimeSpan TransactionDeadline { get; init; }
    /// <summary>Gets maximum observation wait deadline.</summary>
    public required TimeSpan ObservationWaitDeadline { get; init; }
    /// <summary>Gets maximum renewal deadline.</summary>
    public required TimeSpan RenewalDeadline { get; init; }
    /// <summary>Gets maximum commit-observation deadline.</summary>
    public required TimeSpan CommitObservationDeadline { get; init; }
    /// <summary>Gets maximum receipt-resolution deadline.</summary>
    public required TimeSpan ReceiptResolutionDeadline { get; init; }
    /// <summary>Gets maximum maintenance deadline.</summary>
    public required TimeSpan MaintenanceDeadline { get; init; }
    /// <summary>Gets maximum shutdown-drain deadline.</summary>
    public required TimeSpan ShutdownDrainDeadline { get; init; }
    /// <summary>Gets retained non-cooperative provider capacity.</summary>
    public required int ProviderQuarantineSlots { get; init; }
    /// <summary>Gets retained non-cooperative handler capacity.</summary>
    public required int HandlerQuarantineSlots { get; init; }
    /// <summary>Gets the closed provider backup contribution modes.</summary>
    public required ImmutableArray<BaseActivationBackupMode> BackupModes { get; init; }
    /// <summary>Gets the closed provider restore modes.</summary>
    public required ImmutableArray<BaseActivationRestoreMode> RestoreModes { get; init; }
    /// <summary>Gets the canonical capability checksum.</summary>
    public required ImmutableArray<byte> CanonicalChecksum { get; init; }
}

/// <summary>Identifies how durable activation authority participates in backup.</summary>
public enum BaseActivationBackupMode
{
    /// <summary>Activation authority is captured atomically in the authenticated whole-store artifact.</summary>
    WholeStoreAtomic,
}

/// <summary>Identifies certified activation restore behavior.</summary>
public enum BaseActivationRestoreMode
{
    /// <summary>Restores into the same disaster domain while fencing pre-restore claims.</summary>
    InPlaceRecovery,
    /// <summary>Restores into a new authenticated disaster domain with an external occurrence floor.</summary>
    NewDisasterDomain,
}

/// <summary>Validates and supplies the built-in durable-activation capability contract.</summary>
public static class BaseActivationCapabilityContract
{
    /// <summary>Creates the certified built-in semantic envelope.</summary>
    public static BaseActivationProviderCapability BuiltIn(string checksumPurpose) => new()
    {
        AtomicCreationSupported = true, SelectionTargetSupported = true, ModuleTargetSupported = true,
        GuardedChildrenSupported = true, DurableYieldSupported = true, RestoreFencingSupported = true,
        DueInvalidation = BaseDueInvalidationClass.BoundedPolling,
        ScheduleKinds = [BaseScheduleKind.Once, BaseScheduleKind.Interval, BaseScheduleKind.Cron, BaseScheduleKind.Calendar],
        ExecutionClasses = [BaseActivationExecutionClass.TransactionalOperation, BaseActivationExecutionClass.AtLeastOnceWorker, BaseActivationExecutionClass.AtMostOnceEffect],
        MaximumActivationsPerTransaction = 256, MaximumDueCandidates = 256,
        MaximumReadIntervals = 4096, MaximumIndexOperations = 4096,
        MaximumInputBytes = 4L * 1024 * 1024, MaximumResultBytes = 4L * 1024 * 1024,
        MaximumEvidenceBytes = 16L * 1024 * 1024, MaximumTransientBytes = 16L * 1024 * 1024,
        MaximumReceiptBytes = 16L * 1024 * 1024, MaximumPendingRows = 1_000_000,
        MaximumClaimedRows = 1_000_000, MaximumTerminalRows = 1_000_000,
        MaximumAttempts = 1024, MaximumYieldsPerActivation = 1_000_000,
        MaximumReservedYieldReceiptSlots = 1_000_000_000_000,
        MaximumRenewalsPerSlice = 4096, MaximumChildrenPerSlice = 4096,
        MaximumLineageDepth = 256, MaximumOccurrencePage = 256, MaximumTimeZoneBytes = 64L * 1024 * 1024,
        MaximumPriorityAgingBoost = 32, PriorityAgingInterval = TimeSpan.FromMinutes(1),
        ObservationTokenLifetime = TimeSpan.FromMinutes(5),
        MaximumHandlerDependencies = 4096,
        AcquisitionDeadline = TimeSpan.FromSeconds(5), TransactionDeadline = TimeSpan.FromSeconds(30),
        ObservationWaitDeadline = TimeSpan.FromMinutes(5), RenewalDeadline = TimeSpan.FromSeconds(5),
        CommitObservationDeadline = TimeSpan.FromSeconds(30), ReceiptResolutionDeadline = TimeSpan.FromSeconds(30),
        MaintenanceDeadline = TimeSpan.FromMinutes(5), ShutdownDrainDeadline = TimeSpan.FromSeconds(60),
        ProviderQuarantineSlots = 32, HandlerQuarantineSlots = 32,
        BackupModes = [BaseActivationBackupMode.WholeStoreAtomic],
        RestoreModes = [BaseActivationRestoreMode.InPlaceRecovery, BaseActivationRestoreMode.NewDisasterDomain],
        CanonicalChecksum = ImmutableArray.CreateRange(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(checksumPurpose))),
    };

    /// <summary>Returns whether the capability is a closed valid safety envelope.</summary>
    public static bool IsValid(BaseActivationProviderCapability? value) => value is not null
        && value.AtomicCreationSupported && value.GuardedChildrenSupported && value.RestoreFencingSupported
        && Enum.IsDefined(value.DueInvalidation)
        && !value.ScheduleKinds.IsDefaultOrEmpty && value.ScheduleKinds.Distinct().Count() == value.ScheduleKinds.Length
        && !value.ExecutionClasses.IsDefaultOrEmpty && value.ExecutionClasses.Distinct().Count() == value.ExecutionClasses.Length
        && value.MaximumActivationsPerTransaction is >= 1 and <= 256
        && value.MaximumDueCandidates is >= 1 and <= 256
        && value.MaximumReadIntervals is >= 1 and <= 4096
        && value.MaximumIndexOperations is >= 1 and <= 4096
        && value.MaximumInputBytes is >= 1 and <= 4L * 1024 * 1024
        && value.MaximumResultBytes is >= 1 and <= 4L * 1024 * 1024
        && value.MaximumReceiptBytes is >= 1 and <= 16L * 1024 * 1024
        && value.MaximumEvidenceBytes is >= 1 and <= 16L * 1024 * 1024
        && value.MaximumTransientBytes is >= 1 and <= 16L * 1024 * 1024
        && value.MaximumPendingRows is >= 1 and <= 1_000_000
        && value.MaximumClaimedRows is >= 1 and <= 1_000_000
        && value.MaximumTerminalRows is >= 1 and <= 1_000_000
        && value.MaximumAttempts is >= 1 and <= 1024
        && (value.DurableYieldSupported
            ? value.MaximumYieldsPerActivation is >= 1 and <= 1_000_000
                && value.MaximumReservedYieldReceiptSlots is >= 2 and <= 1_000_000_000_000
            : value.MaximumYieldsPerActivation == 0 && value.MaximumReservedYieldReceiptSlots == 0)
        && value.MaximumRenewalsPerSlice is >= 1 and <= 4096
        && value.MaximumChildrenPerSlice is >= 1 and <= 4096
        && value.MaximumLineageDepth is >= 1 and <= 256
        && value.MaximumHandlerDependencies is >= 1 and <= 4096
        && value.MaximumOccurrencePage is >= 1 and <= 256
        && value.MaximumTimeZoneBytes is >= 1 and <= 64L * 1024 * 1024
        && value.MaximumPriorityAgingBoost is >= 1 and <= 32
        && value.PriorityAgingInterval > TimeSpan.Zero && value.PriorityAgingInterval <= TimeSpan.FromMinutes(1)
        && value.ObservationTokenLifetime > TimeSpan.Zero && value.ObservationTokenLifetime <= TimeSpan.FromMinutes(5)
        && value.AcquisitionDeadline > TimeSpan.Zero && value.AcquisitionDeadline <= TimeSpan.FromSeconds(5)
        && value.TransactionDeadline > TimeSpan.Zero && value.TransactionDeadline <= TimeSpan.FromSeconds(30)
        && value.ObservationWaitDeadline > TimeSpan.Zero && value.ObservationWaitDeadline <= TimeSpan.FromMinutes(5)
        && value.RenewalDeadline > TimeSpan.Zero && value.RenewalDeadline <= TimeSpan.FromSeconds(5)
        && value.CommitObservationDeadline > TimeSpan.Zero && value.CommitObservationDeadline <= TimeSpan.FromSeconds(30)
        && value.ReceiptResolutionDeadline > TimeSpan.Zero && value.ReceiptResolutionDeadline <= TimeSpan.FromSeconds(30)
        && value.MaintenanceDeadline > TimeSpan.Zero && value.MaintenanceDeadline <= TimeSpan.FromMinutes(5)
        && value.ShutdownDrainDeadline > TimeSpan.Zero && value.ShutdownDrainDeadline <= TimeSpan.FromSeconds(60)
        && value.ProviderQuarantineSlots > 0 && value.HandlerQuarantineSlots > 0
        && !value.BackupModes.IsDefault && value.BackupModes.Distinct().Count() == value.BackupModes.Length
        && value.BackupModes.All(Enum.IsDefined)
        && !value.RestoreModes.IsDefault && value.RestoreModes.Distinct().Count() == value.RestoreModes.Length
        && value.RestoreModes.All(Enum.IsDefined)
        && value.CanonicalChecksum.Length == 32;

    internal static void Require(BaseActivationProviderCapability capability, BaseActivationDefinition definition)
    {
        if (!IsValid(capability) || !capability.ExecutionClasses.Contains(definition.ExecutionClass)
            || definition.TransactionalTarget is BaseSelectionMutationActivationTarget && !capability.SelectionTargetSupported
            || definition.TransactionalTarget is BaseModuleMutationActivationTarget && !capability.ModuleTargetSupported
            || definition.Limits.MaximumInputBytes > capability.MaximumInputBytes
            || definition.Limits.MaximumResultBytes > capability.MaximumResultBytes
            || definition.Limits.Provider.MaximumEvidenceBytes > capability.MaximumEvidenceBytes
            || definition.Limits.Provider.MaximumTransientBytes > capability.MaximumTransientBytes
            || definition.Limits.Provider.MaximumCandidates > capability.MaximumDueCandidates
            || definition.Limits.Provider.MaximumReadIntervals > capability.MaximumReadIntervals
            || definition.Limits.Provider.MaximumIndexOperations > capability.MaximumIndexOperations
            || definition.Limits.Provider.AcquisitionTimeout > capability.AcquisitionDeadline
            || definition.Limits.Provider.TransactionTimeout > capability.TransactionDeadline
            || definition.Limits.Provider.CommitObservationTimeout > capability.CommitObservationDeadline
            || definition.Limits.Provider.ReceiptResolutionTimeout > capability.ReceiptResolutionDeadline
            || definition.Limits.MaximumAttempts > capability.MaximumAttempts
            || definition.Limits.MaximumYields > 0 && !capability.DurableYieldSupported
            || definition.Limits.MaximumYields > capability.MaximumYieldsPerActivation
            || definition.Limits.MaximumYields > 0 && definition.Limits.MaximumYields + 1 > capability.MaximumReservedYieldReceiptSlots
            || definition.Limits.MaximumRenewalsPerSlice > capability.MaximumRenewalsPerSlice
            || definition.Limits.MaximumChildrenPerSlice > capability.MaximumChildrenPerSlice
            || definition.Limits.MaximumLineageDepth > capability.MaximumLineageDepth)
            throw new InvalidOperationException("base.activation.capabilityUnavailable");
    }

    internal static void Require(BaseActivationProviderCapability capability, BaseScheduleDefinition definition)
    {
        BaseScheduleKind kind = definition.Expression switch
        {
            BaseOnceSchedule => BaseScheduleKind.Once, BaseIntervalSchedule => BaseScheduleKind.Interval,
            BaseCronSchedule => BaseScheduleKind.Cron, BaseCalendarSchedule => BaseScheduleKind.Calendar,
            _ => throw new InvalidOperationException("base.activation.scheduleInvalid"),
        };
        if (!IsValid(capability) || !capability.ScheduleKinds.Contains(kind))
            throw new InvalidOperationException("base.activation.capabilityUnavailable");
    }
}

/// <summary>Defines provider-neutral durable activation operations.</summary>
public interface IBaseActivationProvider
{
    /// <summary>Gets the immutable provider descriptor.</summary>
    BaseActivationProviderDescriptor Descriptor { get; }

    /// <summary>Reads the current checksum-validated durable-yield reservation authority.</summary>
    ValueTask<OperationResult<BaseActivationYieldReservationState>> ReadYieldReservationStateAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Reads every durable definition version required for safe application readiness.</summary>
    ValueTask<OperationResult<BaseActivationDependencyResult>> ReadDependenciesAsync(
        BaseActivationDependencyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Observes the earliest due activation under exact authority.</summary>
    ValueTask<OperationResult<BaseActivationDueObservation>> ObserveDueAsync(
        BaseActivationDueObservationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Waits for a finite due observation to change.</summary>
    ValueTask<BaseDueWaitResult> WaitForDueChangeAsync(
        BaseDueObservationToken token,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically seeks and claims the first eligible activation.</summary>
    ValueTask<OperationResult<BaseActivationClaimResult>> TryClaimNextAsync(
        BaseActivationClaimRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the exact handler-free activation selected for transactional execution.</summary>
    ValueTask<OperationResult<BaseTransactionalActivationCandidate>> ReadTransactionalCandidateAsync(
        BaseTransactionalActivationCandidateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Renews one current stable claim.</summary>
    ValueTask<OperationResult<BaseActivationRenewResult>> RenewAsync(
        BaseActivationRenewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Registers one durable executor incarnation.</summary>
    ValueTask<OperationResult<BaseExecutorRegistrationResult>> RegisterExecutorAsync(
        BaseExecutorRegistrationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Renews one current executor heartbeat.</summary>
    ValueTask<OperationResult<BaseExecutorHeartbeatResult>> HeartbeatExecutorAsync(
        BaseExecutorHeartbeatRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Retires one exact executor incarnation.</summary>
    ValueTask<OperationResult<BaseExecutorRetirementResult>> RetireExecutorAsync(
        BaseExecutorRetirementRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Applies one closed activation state transition.</summary>
    ValueTask<OperationResult<BaseActivationTransitionResult>> TransitionAsync(
        BaseActivationTransitionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one exact current durable schedule authority.</summary>
    ValueTask<OperationResult<BaseScheduleAuthority>> ReadScheduleAsync(
        string scheduleId,
        int scheduleVersion,
        CancellationToken cancellationToken = default);

    /// <summary>Applies one identified schedule create/update/state mutation.</summary>
    ValueTask<OperationResult<BaseScheduleMutationResult>> MutateScheduleAsync(
        BaseScheduleMutationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically applies one Runtime-computed occurrence page.</summary>
    ValueTask<OperationResult<BaseScheduleMaintenancePage>> AdvanceSchedulesAsync(
        BaseScheduleMaintenanceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Advances one crash-recoverable cancel-previous maintenance page.</summary>
    ValueTask<OperationResult<BaseScheduleCancellationMaintenancePage>> AdvanceScheduleCancellationAsync(
        BaseScheduleCancellationMaintenanceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one bounded protected activation-administration page.</summary>
    ValueTask<OperationResult<BaseActivationAdministrationPage>> ReadAdministrationAsync(
        BaseActivationAdministrationQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one exact live activation under installed migration authority.</summary>
    ValueTask<OperationResult<BaseActivationMigrationCandidate>> ReadMigrationCandidateAsync(
        BaseActivationMigrationCandidateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically migrates one live activation to one installed replacement definition.</summary>
    ValueTask<OperationResult<BaseActivationMigrationResult>> MigrateAsync(
        BaseActivationMigrationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Compacts one identified bounded page of expired activation-instance receipts.</summary>
    ValueTask<OperationResult<BaseActivationReceiptCompactionResult>> CompactActivationReceiptsAsync(
        BaseActivationReceiptCompactionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Captures current provider-owned reservation and protected-backup compaction authority.</summary>
    ValueTask<OperationResult<BaseActivationReceiptCompactionAuthority>> CaptureReceiptCompactionAuthorityAsync(
        BaseActivationReceiptCompactionAuthorityRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves one durable receipt without re-executing its operation.</summary>
    ValueTask<OperationResult<BaseActivationReceiptResolution>> ResolveReceiptAsync(
        BaseActivationReceiptResolutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Advances one identified crash-recovery maintenance page.</summary>
    ValueTask<OperationResult<BaseActivationMaintenancePage>> AdvanceMaintenanceAsync(
        BaseActivationMaintenanceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Prunes one identified page of dependency-free disposed activation authority.</summary>
    ValueTask<OperationResult<BaseActivationPrunePage>> PruneAsync(
        BaseActivationPruneRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves one exact indeterminate external-effect outcome.</summary>
    ValueTask<OperationResult<BaseActivationIndeterminateResolution>> ResolveIndeterminateAsync(
        BaseActivationIndeterminateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one bounded sanitized provider-quarantine page.</summary>
    ValueTask<OperationResult<BaseActivationQuarantinePage>> ReadQuarantineAsync(
        BaseActivationQuarantineRequest request,
        CancellationToken cancellationToken = default);
}
