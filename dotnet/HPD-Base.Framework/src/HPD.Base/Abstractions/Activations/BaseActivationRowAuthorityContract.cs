using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Validates the complete closed authority of one provider activation row.</summary>
internal static class BaseActivationRowAuthorityContract
{
    internal static bool IsValid(
        string activationId,
        BaseActivationState state,
        long generation,
        long effectiveDueAt,
        ReadOnlySpan<byte> controlChecksum,
        int attemptNumber,
        long executionSliceOrdinal,
        long? attemptStartedAt,
        long? sliceStartedAt,
        long yieldCount,
        long maximumYields,
        BaseActivationYieldDisposition? terminalYieldDisposition,
        string? terminalYieldFailureCode,
        long claimEpoch,
        byte[]? claimFence,
        string? claimWorker,
        long? leaseRevision,
        long? leaseExpiresAt,
        bool eligible,
        BaseScheduleOverlapPolicy overlapPolicy,
        ReadOnlySpan<byte> definitionChecksum,
        BaseEffectExecutionAuthority? effect)
    {
        if (!Enum.IsDefined(state) || !Enum.IsDefined(overlapPolicy)
            || attemptNumber < 0 || claimEpoch < 0 || definitionChecksum.Length != 32
            || !BaseActivationControlChecksumContract.Matches(controlChecksum, activationId, generation,
                state, effectiveDueAt, yieldCount, maximumYields, executionSliceOrdinal,
                attemptStartedAt, sliceStartedAt, terminalYieldDisposition, terminalYieldFailureCode))
            return false;

        bool countersAbsent = attemptNumber == 0 && executionSliceOrdinal == 0
            && claimEpoch == 0 && attemptStartedAt is null && sliceStartedAt is null;
        bool countersPresent = attemptNumber > 0 && attemptNumber <= executionSliceOrdinal
            && claimEpoch == executionSliceOrdinal
            && attemptStartedAt is not null && sliceStartedAt is not null;
        if (!countersAbsent && !countersPresent)
            return false;

        bool claimed = state == BaseActivationState.Claimed;
        bool completeClaim = claimFence?.Length == 32 && !string.IsNullOrWhiteSpace(claimWorker)
            && leaseRevision is > 0 && leaseExpiresAt is >= 0;
        bool absentClaim = claimFence is null && claimWorker is null
            && leaseRevision is null && leaseExpiresAt is null;
        if (claimed ? !completeClaim || attemptNumber < 1 || claimEpoch < 1 : !absentClaim)
            return false;

        bool expectedEligible = state is BaseActivationState.RetryPending
            or BaseActivationState.YieldPending or BaseActivationState.Claimed;
        if (state != BaseActivationState.Pending && eligible != expectedEligible)
            return false;

        bool effectRequired = state is BaseActivationState.EffectStarted or BaseActivationState.OutcomeUnknown;
        bool effectForbidden = state is BaseActivationState.Pending or BaseActivationState.RetryPending
            or BaseActivationState.YieldPending or BaseActivationState.Claimed
            or BaseActivationState.Cancelled or BaseActivationState.Migrated;
        if (effectRequired && effect is null || effectForbidden && effect is not null)
            return false;
        return effect is null || EffectIsValid(effect, activationId, definitionChecksum, generation,
            attemptNumber, executionSliceOrdinal, attemptStartedAt, sliceStartedAt, yieldCount,
            maximumYields, claimEpoch);
    }

    private static bool EffectIsValid(
        BaseEffectExecutionAuthority effect,
        string activationId,
        ReadOnlySpan<byte> definitionChecksum,
        long rowGeneration,
        int attemptNumber,
        long executionSliceOrdinal,
        long? attemptStartedAt,
        long? sliceStartedAt,
        long yieldCount,
        long maximumYields,
        long claimEpoch)
    {
        BaseActivationClaimAuthority claim = effect.Claim;
        BaseExecutorIncarnationAuthority executor = effect.Executor;
        if (claim.ActivationId != activationId || claim.AttemptNumber != attemptNumber
            || claim.ActivationGeneration < 1 || claim.ActivationGeneration > rowGeneration
            || claim.ExecutionSliceOrdinal != executionSliceOrdinal
            || claim.AttemptStartedAt != attemptStartedAt || claim.SliceStartedAt != sliceStartedAt
            || claim.YieldCount != yieldCount || claim.MaximumYields != maximumYields
            || claim.ClaimEpoch != claimEpoch || claim.FencingToken.Length != 32
            || string.IsNullOrWhiteSpace(claim.WorkerIdentity) || claim.CancellationGeneration < 0
            || string.IsNullOrWhiteSpace(claim.StoreInstanceId) || claim.RestoreEpoch < 0
            || claim.DefinitionChecksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(claim.DefinitionChecksum.AsSpan(), definitionChecksum)
            || string.IsNullOrWhiteSpace(executor.ApplicationId) || string.IsNullOrWhiteSpace(executor.HostId)
            || string.IsNullOrWhiteSpace(executor.ProcessIncarnationId) || executor.ExecutorGeneration < 1
            || string.IsNullOrWhiteSpace(executor.StoreInstanceId) || executor.RestoreEpoch < 0
            || executor.WorkerDefinitionSetChecksum.Length != 32 || executor.Checksum.Length != 32
            || effect.EffectStartGeneration < 1 || effect.EffectStartGeneration > rowGeneration
            || effect.HeartbeatRevision < 1 || effect.HeartbeatExpiresAt < 0 || effect.Checksum.Length != 32)
            return false;

        if (!BaseActivationClaimChecksumContract.Matches(claim.FencingToken.AsSpan(),
            claim.ActivationId, claim.AttemptNumber, claim.ClaimEpoch,
            claim.ExecutionSliceOrdinal, claim.AttemptStartedAt, claim.SliceStartedAt,
            claim.YieldCount, claim.MaximumYields, claim.WorkerIdentity))
            return false;

        byte[] expectedExecutor = Hash($"base.activation.executor.v2\0{executor.ApplicationId}\n{executor.HostId}\n{executor.ProcessIncarnationId}\n{executor.ExecutorGeneration}\n{executor.StoreInstanceId}\n{executor.RestoreEpoch}\n{Convert.ToHexString(executor.WorkerDefinitionSetChecksum.AsSpan())}");
        if (!CryptographicOperations.FixedTimeEquals(expectedExecutor, executor.Checksum.AsSpan()))
            return false;
        byte[] expectedEffect = Hash($"base.activation.effect.v2\0{claim.ActivationId}\n{Convert.ToHexString(claim.FencingToken.AsSpan())}\n{Convert.ToHexString(executor.Checksum.AsSpan())}\n{effect.EffectStartGeneration}\n{effect.HeartbeatRevision}\n{effect.HeartbeatExpiresAt}");
        return CryptographicOperations.FixedTimeEquals(expectedEffect, effect.Checksum.AsSpan());
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
