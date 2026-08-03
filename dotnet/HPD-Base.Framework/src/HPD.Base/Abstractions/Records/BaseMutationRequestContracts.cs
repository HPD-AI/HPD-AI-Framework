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
