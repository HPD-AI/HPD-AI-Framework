using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>A validated immutable 256-bit application mutation fingerprint.</summary>
public sealed class BaseMutationRequestFingerprint : IEquatable<BaseMutationRequestFingerprint>
{
    /// <summary>Gets the required fingerprint length in bytes.</summary>
    public const int Length = 32;

    private readonly byte[] _value;

    private BaseMutationRequestFingerprint(byte[] value) => _value = value;

    /// <summary>Creates a fingerprint by defensively copying exactly 32 bytes.</summary>
    public static BaseMutationRequestFingerprint Create(ReadOnlySpan<byte> value)
    {
        if (value.Length != Length)
        {
            throw new ArgumentException(
                $"A mutation request fingerprint must contain exactly {Length} bytes.",
                nameof(value));
        }

        return new BaseMutationRequestFingerprint(value.ToArray());
    }

    /// <summary>Copies the fingerprint into a caller-owned destination.</summary>
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException(
                $"The destination must contain at least {Length} bytes.",
                nameof(destination));
        }

        _value.CopyTo(destination);
    }

    /// <summary>Returns a caller-owned copy of the fingerprint bytes.</summary>
    public byte[] ToArray() => [.. _value];

    /// <inheritdoc />
    public bool Equals(BaseMutationRequestFingerprint? other) =>
        other is not null && CryptographicOperations.FixedTimeEquals(_value, other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as BaseMutationRequestFingerprint);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (byte value in _value)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Identifies one application-level atomic mutation request.</summary>
public sealed record BaseMutationRequestIdentity
{
    /// <summary>Gets the normalized application scope.</summary>
    public required string Scope { get; init; }

    /// <summary>Gets the normalized product operation name.</summary>
    public required string Operation { get; init; }

    /// <summary>Gets the normalized idempotency key.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Gets the immutable application fingerprint.</summary>
    public required BaseMutationRequestFingerprint Fingerprint { get; init; }

    /// <summary>Creates and validates one normalized request identity.</summary>
    public static BaseMutationRequestIdentity Create(
        string scope,
        string operation,
        string idempotencyKey,
        BaseMutationRequestFingerprint fingerprint) =>
        new()
        {
            Scope = Normalize(scope, 128, nameof(scope)),
            Operation = Normalize(operation, 128, nameof(operation)),
            IdempotencyKey = Normalize(idempotencyKey, 256, nameof(idempotencyKey)),
            Fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint)),
        };

    private static string Normalize(string value, int maximumUtf8Bytes, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        string normalized = value.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length == 0 ||
            normalized.Any(char.IsControl) ||
            System.Text.Encoding.UTF8.GetByteCount(normalized) > maximumUtf8Bytes)
        {
            throw new ArgumentException("The mutation request identity value is invalid.", parameterName);
        }

        return normalized;
    }
}

/// <summary>Reports whether an atomic request newly committed or resolved a prior commit.</summary>
public enum BaseMutationRequestDisposition
{
    /// <summary>The request newly committed.</summary>
    Committed,

    /// <summary>The same request had already committed.</summary>
    Duplicate
}

/// <summary>Classifies how long atomic request receipts survive.</summary>
public enum BaseAtomicRequestDurability
{
    /// <summary>Atomic request receipts are unavailable.</summary>
    None,
    /// <summary>Receipts survive only for the current process.</summary>
    ProcessLocal,
    /// <summary>Receipts survive provider and process restart.</summary>
    Durable
}

/// <summary>Declares a store's atomic request receipt guarantees and bounds.</summary>
public sealed record AtomicRequestCapability
{
    /// <summary>Gets whether identified atomic requests are supported.</summary>
    public required bool Supported { get; init; }
    /// <summary>Gets the receipt durability classification.</summary>
    public required BaseAtomicRequestDurability Durability { get; init; }
    /// <summary>Gets whether duplicates return the stored committed result.</summary>
    public required bool DuplicateResultReplay { get; init; }
    /// <summary>Gets whether fingerprint conflicts are detected.</summary>
    public required bool FingerprintConflictDetection { get; init; }
    /// <summary>Gets whether an indeterminate commit can be resolved by exact retry.</summary>
    public required bool IndeterminateResolution { get; init; }
    /// <summary>Gets the maximum normalized identity size in UTF-8 bytes.</summary>
    public required int MaxIdentityBytes { get; init; }
    /// <summary>Gets the maximum stored canonical receipt size.</summary>
    public required int MaxReceiptBytes { get; init; }
    /// <summary>Gets the minimum supported receipt lifetime.</summary>
    public required TimeSpan MinReceiptLifetime { get; init; }
    /// <summary>Gets the maximum supported receipt lifetime.</summary>
    public required TimeSpan MaxReceiptLifetime { get; init; }
}

/// <summary>Stable errors for identified atomic mutation requests.</summary>
public static class BaseMutationRequestErrorCodes
{
    /// <summary>The request identity is invalid.</summary>
    public const string Invalid = "base.runtime.request.invalid";
    /// <summary>The selected store does not support identified requests.</summary>
    public const string Unsupported = "base.runtime.request.unsupported";
    /// <summary>The identity was reused with different bound semantics.</summary>
    public const string FingerprintConflict = "base.runtime.request.fingerprintConflict";
    /// <summary>The canonical receipt exceeds its configured bound.</summary>
    public const string ReceiptTooLarge = "base.runtime.request.receiptTooLarge";
    /// <summary>The stored receipt cannot be projected through the current schema.</summary>
    public const string ReceiptUnavailable = "base.runtime.request.receiptUnavailable";
    /// <summary>The provider cannot yet determine whether the request committed.</summary>
    public const string OutcomeUnknown = "base.runtime.request.outcomeUnknown";
}
