using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Owns the canonical provider-independent checksum for activation control state.</summary>
public static class BaseActivationControlChecksumContract
{
    /// <summary>Creates the canonical checksum for one exact activation control state.</summary>
    /// <param name="activationId">The stable activation identity.</param>
    /// <param name="generation">The positive control generation.</param>
    /// <param name="state">The exact durable activation state.</param>
    /// <returns>The immutable 32-byte SHA-256 checksum.</returns>
    public static ImmutableArray<byte> Create(
        string activationId,
        long generation,
        BaseActivationState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        if (generation < 1)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        return SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.activation.control.v2\0{activationId}\n{generation}\n{(int)state}"))
            .ToImmutableArray();
    }

    /// <summary>Validates one untrusted checksum against canonical control authority.</summary>
    /// <param name="checksum">The untrusted checksum.</param>
    /// <param name="activationId">The expected activation identity.</param>
    /// <param name="generation">The expected control generation.</param>
    /// <param name="state">The expected durable activation state.</param>
    /// <returns><see langword="true"/> only for an exact fixed-time match.</returns>
    public static bool Matches(
        ReadOnlySpan<byte> checksum,
        string activationId,
        long generation,
        BaseActivationState state)
    {
        if (checksum.Length != SHA256.HashSizeInBytes || string.IsNullOrWhiteSpace(activationId)
            || generation < 1 || !Enum.IsDefined(state))
            return false;
        ImmutableArray<byte> expected = Create(activationId, generation, state);
        return CryptographicOperations.FixedTimeEquals(checksum, expected.AsSpan());
    }
}
