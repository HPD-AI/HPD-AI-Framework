using System.Collections.Immutable;
using FluentAssertions;

namespace HPD.Base.Tests;

public sealed class SemanticRetirementPruneContinuityTests
{
    [Theory]
    [InlineData(BaseActivationState.Succeeded)]
    [InlineData(BaseActivationState.Exhausted)]
    [InlineData(BaseActivationState.Cancelled)]
    [InlineData(BaseActivationState.Migrated)]
    public void Later_disposed_prune_floor_dominates_an_earlier_eligible_terminal_fact(
        BaseActivationState terminalState)
    {
        BaseSemanticActivationRetirementAuthority retired = Retirement(terminalState, generation: 7);
        BaseActivationPruneEvidence prune = Prune(generation: 8);

        BaseSemanticActivationEvidenceContract
            .PruneEvidenceDominatesRetirement(prune, retired)
            .Should().BeTrue();
    }

    [Fact]
    public void Equal_generation_requires_exact_control_and_receipt_authority()
    {
        BaseSemanticActivationRetirementAuthority retired =
            Retirement(BaseActivationState.Disposed, generation: 7);

        BaseSemanticActivationEvidenceContract.PruneEvidenceDominatesRetirement(
            Prune(generation: 7), retired).Should().BeTrue();
        BaseSemanticActivationEvidenceContract.PruneEvidenceDominatesRetirement(
            Prune(generation: 7) with { TerminalControlChecksum = Bytes(0x31) }, retired)
            .Should().BeFalse();
        BaseSemanticActivationEvidenceContract.PruneEvidenceDominatesRetirement(
            Prune(generation: 7) with { TerminalReceiptChecksum = Bytes(0x41) }, retired)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(BaseActivationState.Succeeded)]
    [InlineData(BaseActivationState.Exhausted)]
    [InlineData(BaseActivationState.Cancelled)]
    [InlineData(BaseActivationState.Migrated)]
    public void Equal_generation_is_rejected_until_the_retired_activation_was_already_disposed(
        BaseActivationState terminalState)
    {
        BaseSemanticActivationRetirementAuthority retired = Retirement(terminalState, generation: 7);

        BaseSemanticActivationEvidenceContract.PruneEvidenceDominatesRetirement(
            Prune(generation: 7), retired).Should().BeFalse();
    }

    [Theory]
    [InlineData(BaseActivationState.Succeeded)]
    [InlineData(BaseActivationState.Exhausted)]
    [InlineData(BaseActivationState.Cancelled)]
    [InlineData(BaseActivationState.Migrated)]
    public void More_than_the_single_disposal_generation_is_rejected(
        BaseActivationState terminalState)
    {
        BaseSemanticActivationRetirementAuthority retired = Retirement(terminalState, generation: 7);

        BaseSemanticActivationEvidenceContract.PruneEvidenceDominatesRetirement(
            Prune(generation: 9), retired).Should().BeFalse();
    }

    [Theory]
    [InlineData(BaseActivationState.Succeeded)]
    [InlineData(BaseActivationState.Exhausted)]
    [InlineData(BaseActivationState.Cancelled)]
    [InlineData(BaseActivationState.Migrated)]
    public void Maximum_retirement_generation_cannot_admit_an_overflowed_disposal_generation(
        BaseActivationState terminalState)
    {
        BaseSemanticActivationRetirementAuthority retired = Retirement(terminalState, generation: long.MaxValue);

        BaseSemanticActivationEvidenceContract.PruneEvidenceDominatesRetirement(
            Prune(generation: long.MaxValue), retired).Should().BeFalse();
    }

    [Fact]
    public void Lower_generation_and_later_generation_after_disposed_retirement_are_rejected()
    {
        BaseSemanticActivationRetirementAuthority exhausted =
            Retirement(BaseActivationState.Exhausted, generation: 7);
        BaseSemanticActivationRetirementAuthority disposed =
            Retirement(BaseActivationState.Disposed, generation: 7);

        BaseSemanticActivationEvidenceContract.PruneEvidenceDominatesRetirement(
            Prune(generation: 6), exhausted).Should().BeFalse();
        BaseSemanticActivationEvidenceContract.PruneEvidenceDominatesRetirement(
            Prune(generation: 8), disposed).Should().BeFalse();
    }

    private static BaseSemanticActivationRetirementAuthority Retirement(
        BaseActivationState state,
        long generation) => new()
    {
        Definition = new BaseSemanticActivationDefinitionKey
        {
            Id = "semantic-definition",
            Version = 1,
            Checksum = Bytes(0x10),
        },
        KeyDigest = BaseSemanticActivationKeyDigest.Create(new byte[32]),
        ScopeBindingId = [],
        ActivationId = "activation-1",
        TerminalState = state,
        TerminalActivationGeneration = generation,
        TerminalActivationChecksum = Bytes(0x30),
        TerminalEffectiveDueAt = 0,
        TerminalYieldCount = 0,
        TerminalMaximumYields = 0,
        TerminalExecutionSliceOrdinal = 1,
        CompletionOperationChecksum = Bytes(0x20),
        CompletionReceiptChecksum = Bytes(0x40),
        RetirementPosition = 1,
        SlotGeneration = 2,
        StoreAuthority = null!,
        Checksum = Bytes(0x50),
    };

    private static BaseActivationPruneEvidence Prune(long generation) => new()
    {
        ActivationId = "activation-1",
        Definition = new BaseActivationDefinitionKey
        {
            Id = "activation-definition",
            Version = 1,
            Checksum = Bytes(0x11),
        },
        TerminalGeneration = generation,
        TerminalControlChecksum = Bytes(0x30),
        TerminalReceiptChecksum = Bytes(0x40),
        OccurrenceChecksum = null,
        ResultChecksum = null,
        PruneAuthorityGeneration = 1,
        ApplicationId = "application",
        LogicalStoreId = "store",
        StoreInstanceId = "instance",
        RestoreEpoch = 0,
        PublicationAuthorityChecksum = Bytes(0x60),
        Checksum = Bytes(0x70),
    };

    private static ImmutableArray<byte> Bytes(byte value) =>
        Enumerable.Repeat(value, 32).ToImmutableArray();
}
