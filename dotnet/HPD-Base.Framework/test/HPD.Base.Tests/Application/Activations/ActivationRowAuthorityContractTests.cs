using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;

namespace HPD.Base.Tests.Application.Activations;

public sealed class ActivationRowAuthorityContractTests
{
    [Fact]
    public void Rejects_undefined_state_despite_matching_unchecked_outer_checksum()
    {
        BaseActivationState state = (BaseActivationState)999;
        byte[] checksum = UncheckedControlChecksum(state, 0, null, null);

        IsValid(state, checksum, eligible: false).Should().BeFalse();
    }

    [Fact]
    public void Rejects_state_incompatible_eligibility_with_valid_outer_checksum()
    {
        BaseActivationState state = BaseActivationState.RetryPending;
        byte[] checksum = BaseActivationControlChecksumContract.Create(
            "activation-1", 2, state, 20, 0, 2, 1, 10, 10, null, null).ToArray();

        IsValid(state, checksum, eligible: false, attempt: 1, slice: 1,
            attemptStartedAt: 10, sliceStartedAt: 10, claimEpoch: 1).Should().BeFalse();
    }

    [Fact]
    public void Rejects_illegal_start_presence_despite_matching_unchecked_outer_checksum()
    {
        BaseActivationState state = BaseActivationState.RetryPending;
        byte[] checksum = UncheckedControlChecksum(state, 1, null, null);

        IsValid(state, checksum, eligible: true, attempt: 1, slice: 1,
            claimEpoch: 1).Should().BeFalse();
    }

    [Fact]
    public void Rejects_positive_attempt_with_zero_slice_despite_matching_unchecked_outer_checksum()
    {
        BaseActivationState state = BaseActivationState.Pending;
        byte[] checksum = UncheckedControlChecksum(state, 0, null, null);

        IsValid(state, checksum, eligible: false, attempt: 5).Should().BeFalse();
    }

    [Fact]
    public void Rejects_attempt_greater_than_slice_with_valid_outer_checksum()
    {
        BaseActivationState state = BaseActivationState.RetryPending;
        byte[] checksum = BaseActivationControlChecksumContract.Create(
            "activation-1", 2, state, 20, 0, 2, 1, 10, 10, null, null).ToArray();

        IsValid(state, checksum, eligible: true, attempt: 2, slice: 1,
            attemptStartedAt: 10, sliceStartedAt: 10, claimEpoch: 1).Should().BeFalse();
    }

    [Fact]
    public void Rejects_claim_epoch_that_does_not_equal_slice()
    {
        BaseActivationState state = BaseActivationState.RetryPending;
        byte[] checksum = BaseActivationControlChecksumContract.Create(
            "activation-1", 2, state, 20, 0, 2, 2, 10, 11, null, null).ToArray();

        IsValid(state, checksum, eligible: true, attempt: 1, slice: 2,
            attemptStartedAt: 10, sliceStartedAt: 11, claimEpoch: 1).Should().BeFalse();
    }

    [Fact]
    public void Rejects_missing_required_effect_with_valid_outer_checksum()
    {
        BaseActivationState state = BaseActivationState.EffectStarted;
        byte[] checksum = BaseActivationControlChecksumContract.Create(
            "activation-1", 2, state, 20, 0, 2, 1, 10, 10, null, null).ToArray();

        IsValid(state, checksum, eligible: false, attempt: 1, slice: 1,
            attemptStartedAt: 10, sliceStartedAt: 10, claimEpoch: 1).Should().BeFalse();
    }

    [Fact]
    public void Rejects_stray_effect_on_runnable_state()
    {
        BaseActivationState state = BaseActivationState.RetryPending;
        byte[] checksum = BaseActivationControlChecksumContract.Create(
            "activation-1", 2, state, 20, 0, 2, 1, 10, 10, null, null).ToArray();

        IsValid(state, checksum, eligible: true, attempt: 1, slice: 1,
            attemptStartedAt: 10, sliceStartedAt: 10, claimEpoch: 1,
            effect: ValidEffect()).Should().BeFalse();
    }

    [Theory]
    [InlineData(BaseActivationState.Cancelled)]
    [InlineData(BaseActivationState.Migrated)]
    public void Rejects_stray_effect_on_terminal_state_that_cannot_retain_one(BaseActivationState state)
    {
        byte[] checksum = BaseActivationControlChecksumContract.Create(
            "activation-1", 2, state, 20, 0, 2, 1, 10, 10, null, null).ToArray();

        IsValid(state, checksum, eligible: false, attempt: 1, slice: 1,
            attemptStartedAt: 10, sliceStartedAt: 10, claimEpoch: 1,
            effect: ValidEffect()).Should().BeFalse();
    }

    [Fact]
    public void Rejects_substituted_claim_field_with_recomputed_effect_checksum()
    {
        BaseEffectExecutionAuthority valid = ValidEffect();
        BaseActivationClaimAuthority substitutedClaim = valid.Claim with { WorkerIdentity = "worker-2" };
        BaseEffectExecutionAuthority substituted = valid with
        {
            Claim = substitutedClaim,
            Checksum = Hash($"base.activation.effect.v2\0{substitutedClaim.ActivationId}\n{Convert.ToHexString(substitutedClaim.FencingToken.AsSpan())}\n{Convert.ToHexString(valid.Executor.Checksum.AsSpan())}\n{valid.EffectStartGeneration}\n{valid.HeartbeatRevision}\n{valid.HeartbeatExpiresAt}").ToImmutableArray(),
        };
        BaseActivationState state = BaseActivationState.EffectStarted;
        byte[] checksum = BaseActivationControlChecksumContract.Create(
            "activation-1", 2, state, 20, 0, 2, 1, 10, 10, null, null).ToArray();

        IsValid(state, checksum, eligible: false, attempt: 1, slice: 1,
            attemptStartedAt: 10, sliceStartedAt: 10, claimEpoch: 1,
            effect: substituted).Should().BeFalse();
    }

    [Fact]
    public void Rejects_substituted_executor_even_when_effect_checksum_is_recomputed()
    {
        BaseEffectExecutionAuthority valid = ValidEffect();
        BaseExecutorIncarnationAuthority substitutedExecutor = valid.Executor with
        { Checksum = SHA256.HashData("substituted-executor"u8).ToImmutableArray() };
        BaseEffectExecutionAuthority substituted = valid with
        {
            Executor = substitutedExecutor,
            Checksum = Hash($"base.activation.effect.v2\0{valid.Claim.ActivationId}\n{Convert.ToHexString(valid.Claim.FencingToken.AsSpan())}\n{Convert.ToHexString(substitutedExecutor.Checksum.AsSpan())}\n{valid.EffectStartGeneration}\n{valid.HeartbeatRevision}\n{valid.HeartbeatExpiresAt}").ToImmutableArray(),
        };
        BaseActivationState state = BaseActivationState.EffectStarted;
        byte[] checksum = BaseActivationControlChecksumContract.Create(
            "activation-1", 2, state, 20, 0, 2, 1, 10, 10, null, null).ToArray();

        IsValid(state, checksum, eligible: false, attempt: 1, slice: 1,
            attemptStartedAt: 10, sliceStartedAt: 10, claimEpoch: 1,
            effect: substituted).Should().BeFalse();
    }

    private static bool IsValid(
        BaseActivationState state,
        byte[] checksum,
        bool eligible,
        int attempt = 0,
        long slice = 0,
        long? attemptStartedAt = null,
        long? sliceStartedAt = null,
        long claimEpoch = 0,
        BaseEffectExecutionAuthority? effect = null) =>
        BaseActivationRowAuthorityContract.IsValid(
            "activation-1", state, 2, 20, checksum, attempt, slice, attemptStartedAt,
            sliceStartedAt, 0, 2, null, null, claimEpoch, null, null, null, null,
            eligible, BaseScheduleOverlapPolicy.Allow, DefinitionChecksum, effect);

    private static BaseEffectExecutionAuthority ValidEffect()
    {
        byte[] fence = BaseActivationClaimChecksumContract.Create(
            "activation-1", 1, 1, 1, 10, 10, 0, 2, "worker-1").ToArray();
        var claim = new BaseActivationClaimAuthority
        {
            ActivationId = "activation-1", AttemptNumber = 1, ActivationGeneration = 1,
            ExecutionSliceOrdinal = 1, AttemptStartedAt = 10, SliceStartedAt = 10,
            YieldCount = 0, MaximumYields = 2, ClaimEpoch = 1,
            FencingToken = fence.ToImmutableArray(), WorkerIdentity = "worker-1",
            CancellationGeneration = 0, StoreInstanceId = "store-instance-1", RestoreEpoch = 0,
            DefinitionChecksum = DefinitionChecksum.ToImmutableArray(),
        };
        byte[] workerSet = SHA256.HashData("workers"u8);
        byte[] executorChecksum = Hash(
            $"base.activation.executor.v2\0application\nhost\nprocess\n1\nstore-instance-1\n0\n{Convert.ToHexString(workerSet)}");
        var executor = new BaseExecutorIncarnationAuthority
        {
            ApplicationId = "application", HostId = "host", ProcessIncarnationId = "process",
            ExecutorGeneration = 1, StoreInstanceId = "store-instance-1", RestoreEpoch = 0,
            WorkerDefinitionSetChecksum = workerSet.ToImmutableArray(),
            Checksum = executorChecksum.ToImmutableArray(),
        };
        return new BaseEffectExecutionAuthority
        {
            Claim = claim, Executor = executor, EffectStartGeneration = 2,
            HeartbeatRevision = 1, HeartbeatExpiresAt = 30,
            Checksum = Hash($"base.activation.effect.v2\0activation-1\n{Convert.ToHexString(fence)}\n{Convert.ToHexString(executorChecksum)}\n2\n1\n30").ToImmutableArray(),
        };
    }

    private static readonly byte[] DefinitionChecksum = SHA256.HashData("definition"u8);

    private static byte[] UncheckedControlChecksum(
        BaseActivationState state,
        long slice,
        long? attemptStartedAt,
        long? sliceStartedAt)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.activation.control.v3\0"u8);
        AppendText(hash, "activation-1");
        AppendInt64(hash, 2);
        AppendInt32(hash, (int)state);
        AppendInt64(hash, 20);
        AppendInt64(hash, 0);
        AppendInt64(hash, 2);
        AppendInt64(hash, slice);
        AppendOptionalInt64(hash, attemptStartedAt);
        AppendOptionalInt64(hash, sliceStartedAt);
        hash.AppendData([0, 0]);
        return hash.GetHashAndReset();
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static void AppendText(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendOptionalInt64(IncrementalHash hash, long? value)
    {
        hash.AppendData([(byte)(value.HasValue ? 1 : 0)]);
        if (value.HasValue) AppendInt64(hash, value.Value);
    }
}
