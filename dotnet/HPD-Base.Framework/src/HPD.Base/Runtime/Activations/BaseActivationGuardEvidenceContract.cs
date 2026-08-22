using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal static class BaseActivationGuardEvidenceContract
{
    private static readonly byte[] Purpose = "base.activation.guard.v3\0"u8.ToArray();

    internal static BaseCapturedActivationGuardEvidence Create(
        BaseActivationGuard guard,
        long generation,
        long leaseRevision,
        long leaseExpiresAt)
    {
        ArgumentNullException.ThrowIfNull(guard);
        return new BaseCapturedActivationGuardEvidence
        {
            ActivationId = Copy(guard.Claim.ActivationId),
            AttemptNumber = guard.Claim.AttemptNumber,
            ClaimEpoch = guard.Claim.ClaimEpoch,
            FencingToken = guard.Claim.FencingToken.ToArray().ToImmutableArray(),
            WorkerIdentity = Copy(guard.Claim.WorkerIdentity),
            CancellationGeneration = guard.Claim.CancellationGeneration,
            StoreInstanceId = Copy(guard.Claim.StoreInstanceId),
            RestoreEpoch = guard.Claim.RestoreEpoch,
            DefinitionChecksum = guard.Claim.DefinitionChecksum.ToArray().ToImmutableArray(),
            StepId = Copy(guard.StepId),
            ChildOrdinal = guard.ChildOrdinal,
            ChildRequestFingerprint = guard.ChildRequestFingerprint.ToArray().ToImmutableArray(),
            Generation = generation,
            LeaseRevision = leaseRevision,
            LeaseExpiresAt = leaseExpiresAt,
            Checksum = Compute(guard, generation, leaseRevision, leaseExpiresAt).ToImmutableArray(),
        };
    }

    internal static bool Matches(BaseActivationGuard? guard, BaseCapturedActivationGuardEvidence? evidence)
    {
        if (guard is null) return evidence is null;
        if (evidence is null) return false;
        BaseActivationClaimAuthority claim = guard.Claim;
        return string.Equals(claim.ActivationId, evidence.ActivationId, StringComparison.Ordinal)
            && claim.AttemptNumber == evidence.AttemptNumber
            && claim.ClaimEpoch == evidence.ClaimEpoch
            && Fixed(claim.FencingToken, evidence.FencingToken)
            && string.Equals(claim.WorkerIdentity, evidence.WorkerIdentity, StringComparison.Ordinal)
            && claim.CancellationGeneration == evidence.CancellationGeneration
            && string.Equals(claim.StoreInstanceId, evidence.StoreInstanceId, StringComparison.Ordinal)
            && claim.RestoreEpoch == evidence.RestoreEpoch
            && Fixed(claim.DefinitionChecksum, evidence.DefinitionChecksum)
            && string.Equals(guard.StepId, evidence.StepId, StringComparison.Ordinal)
            && guard.ChildOrdinal == evidence.ChildOrdinal
            && Fixed(guard.ChildRequestFingerprint, evidence.ChildRequestFingerprint)
            && evidence.Generation > 0 && evidence.LeaseRevision > 0
            && evidence.LeaseExpiresAt > 0
            && evidence.Checksum.Length == 32
            && CryptographicOperations.FixedTimeEquals(
                Compute(guard, evidence.Generation, evidence.LeaseRevision, evidence.LeaseExpiresAt),
                evidence.Checksum.AsSpan());
    }

    private static byte[] Compute(BaseActivationGuard guard, long generation, long leaseRevision, long leaseExpiresAt)
    {
        using var stream = new MemoryStream();
        stream.Write(Purpose);
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            Write(writer, guard.Claim.ActivationId);
            writer.Write(guard.Claim.AttemptNumber);
            writer.Write(guard.Claim.ClaimEpoch);
            Write(writer, guard.Claim.FencingToken);
            Write(writer, guard.Claim.WorkerIdentity);
            writer.Write(guard.Claim.CancellationGeneration);
            Write(writer, guard.Claim.StoreInstanceId);
            writer.Write(guard.Claim.RestoreEpoch);
            Write(writer, guard.Claim.DefinitionChecksum);
            Write(writer, guard.StepId);
            writer.Write(guard.ChildOrdinal);
            Write(writer, guard.ChildRequestFingerprint);
            writer.Write(generation);
            writer.Write(leaseRevision);
            writer.Write(leaseExpiresAt);
        }
        return SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }

    private static void Write(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void Write(BinaryWriter writer, ImmutableArray<byte> value)
    {
        writer.Write(value.Length);
        writer.Write(value.AsSpan());
    }

    private static bool Fixed(ImmutableArray<byte> left, ImmutableArray<byte> right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left.AsSpan(), right.AsSpan());

    private static string Copy(string value) => new(value.AsSpan());
}
