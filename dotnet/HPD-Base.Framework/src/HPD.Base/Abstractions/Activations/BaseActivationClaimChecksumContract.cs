using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Owns the canonical provider-independent activation claim-fence authority.</summary>
internal static class BaseActivationClaimChecksumContract
{
    internal static ImmutableArray<byte> Create(
        string activationId,
        int attemptNumber,
        long claimEpoch,
        long executionSliceOrdinal,
        long attemptStartedAt,
        long sliceStartedAt,
        long yieldCount,
        long maximumYields,
        string workerIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerIdentity);
        if (attemptNumber < 1 || executionSliceOrdinal < attemptNumber || claimEpoch != executionSliceOrdinal
            || attemptStartedAt < 0 || sliceStartedAt < attemptStartedAt
            || maximumYields < 0 || yieldCount < 0 || yieldCount > maximumYields)
            throw new ArgumentException("Activation claim authority is inconsistent.");
        return SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.activation.claim.v3\0{activationId}\n{attemptNumber}\n{claimEpoch}\n{executionSliceOrdinal}\n{attemptStartedAt}\n{sliceStartedAt}\n{yieldCount}\n{maximumYields}\n{workerIdentity}"))
            .ToImmutableArray();
    }

    internal static bool Matches(
        ReadOnlySpan<byte> checksum,
        string activationId,
        int attemptNumber,
        long claimEpoch,
        long executionSliceOrdinal,
        long attemptStartedAt,
        long sliceStartedAt,
        long yieldCount,
        long maximumYields,
        string workerIdentity)
    {
        if (checksum.Length != SHA256.HashSizeInBytes)
            return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(checksum, Create(
                activationId, attemptNumber, claimEpoch, executionSliceOrdinal,
                attemptStartedAt, sliceStartedAt, yieldCount, maximumYields,
                workerIdentity).AsSpan());
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
