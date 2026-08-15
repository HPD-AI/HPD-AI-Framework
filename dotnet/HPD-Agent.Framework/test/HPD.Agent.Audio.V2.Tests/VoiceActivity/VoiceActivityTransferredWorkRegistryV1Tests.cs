using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityTransferredWorkRegistryV1Tests
{
    [Fact]
    public async Task Capacity_and_duplicate_execution_are_bounded_until_terminal_settlement()
    {
        var source = new Source(Capabilities(1));
        var registry = new VoiceActivityTransferredWorkRegistryV1(source);
        var first = Window(OperationId.Create());

        Assert.IsType<VoiceActivityTransferResultV1.Accepted>(await registry.TransferAsync(first, default));
        Assert.IsType<VoiceActivityTransferResultV1.OutcomeUnknown>(await registry.TransferAsync(first, default));
        var saturated = Assert.IsType<VoiceActivityTransferResultV1.Rejected>(
            await registry.TransferAsync(Window(OperationId.Create()), default));
        Assert.Equal(VoiceActivitySourceUnavailableReasonV1.CapacityUnavailable,
            Assert.IsType<VoiceActivitySourceOutcomeV1.Unavailable>(saturated.Outcome).Reason);
        Assert.Equal(1, source.TransferCalls);
        Assert.Equal(1, registry.PendingCount);

        Assert.IsType<VoiceActivitySettlementResultV1.Settled>(
            await registry.SettleAsync(first.OperationId, default));
        Assert.Equal(0, registry.PendingCount);
        Assert.IsType<VoiceActivityTransferResultV1.Accepted>(
            await registry.TransferAsync(Window(OperationId.Create()), default));
    }

    [Fact]
    public async Task Lost_acceptance_acknowledgement_retains_credit_until_queryable_settlement()
    {
        var source = new Source(Capabilities(1)) { LoseTransferAcknowledgement = true };
        var registry = new VoiceActivityTransferredWorkRegistryV1(source);
        var window = Window(OperationId.Create());

        Assert.IsType<VoiceActivityTransferResultV1.OutcomeUnknown>(
            await registry.TransferAsync(window, default));
        Assert.Equal(1, registry.PendingCount);
        Assert.IsType<VoiceActivitySettlementResultV1.Settled>(
            await registry.SettleAsync(window.OperationId, default));
        Assert.Equal(0, registry.PendingCount);
    }

    [Fact]
    public async Task Rejection_releases_credit_but_pending_and_unknown_settlement_do_not()
    {
        var source = new Source(Capabilities(1)) { RejectTransfer = true };
        var registry = new VoiceActivityTransferredWorkRegistryV1(source);
        Assert.IsType<VoiceActivityTransferResultV1.Rejected>(
            await registry.TransferAsync(Window(OperationId.Create()), default));
        Assert.Equal(0, registry.PendingCount);

        source.RejectTransfer = false;
        source.Settlement = SettlementMode.Pending;
        var window = Window(OperationId.Create());
        await registry.TransferAsync(window, default);
        Assert.IsType<VoiceActivitySettlementResultV1.Pending>(
            await registry.SettleAsync(window.OperationId, default));
        Assert.Equal(1, registry.PendingCount);
        source.Settlement = SettlementMode.Unknown;
        Assert.IsType<VoiceActivitySettlementResultV1.OutcomeUnknown>(
            await registry.SettleAsync(window.OperationId, default));
        Assert.Equal(1, registry.PendingCount);
    }

    [Fact]
    public async Task Close_fences_new_work_but_preserves_pending_settlement()
    {
        var source = new Source(Capabilities(1));
        var registry = new VoiceActivityTransferredWorkRegistryV1(source);
        var pending = Window(OperationId.Create());
        await registry.TransferAsync(pending, default);
        registry.Close();

        var rejected = Assert.IsType<VoiceActivityTransferResultV1.Rejected>(
            await registry.TransferAsync(Window(OperationId.Create()), default));
        Assert.Equal(VoiceActivityNoObservationReasonV1.SourceRevoked,
            Assert.IsType<VoiceActivitySourceOutcomeV1.NoObservation>(rejected.Outcome).Reason);
        Assert.IsType<VoiceActivitySettlementResultV1.Settled>(
            await registry.SettleAsync(pending.OperationId, default));
        Assert.Equal(0, registry.PendingCount);
    }

    private static VoiceActivityOwnedWindowV1 Window(OperationId operation) => new(
        operation, new byte[320],
        new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1),
        new VoiceActivityMediaExtentV1(GraphGenerationId.Create(), 1, 2, true),
        new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 1));

    private static VoiceActivitySourceCapabilitiesV1 Capabilities(int maximumPending) => new(
        VoiceActivityInputOwnershipV1.IsolatedTransferred,
        [new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
        new VoiceActivityWindowCapabilityV1(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10), 1),
        new VoiceActivityMeasurementDescriptorV1(VoiceActivityMeasurementKindV1.EngineScore,
            new BoundedAscii("measurement"), -1, 1, null),
        VoiceActivitySourceStateModelV1.StreamLocal, VoiceActivitySourceConcurrencyV1.ParallelWindows,
        VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Sequenced,
        VoiceActivitySourceControlV1.Sequenced, VoiceActivitySourceControlV1.ReplacementRequired,
        true, false, maximumPending);

    private enum SettlementMode { Settled, Pending, Unknown }

    private sealed class Source(VoiceActivitySourceCapabilitiesV1 capabilities) : ITransferredVoiceActivitySourceV1
    {
        private readonly HashSet<OperationId> _accepted = [];
        public VoiceActivitySourceCapabilitiesV1 Capabilities { get; } = capabilities;
        public int TransferCalls { get; private set; }
        public bool LoseTransferAcknowledgement { get; set; }
        public bool RejectTransfer { get; set; }
        public SettlementMode Settlement { get; set; }

        public ValueTask<VoiceActivityTransferResultV1> TransferAsync(
            VoiceActivityOwnedWindowV1 window, CancellationToken cancellationToken)
        {
            TransferCalls++;
            if (RejectTransfer)
                return ValueTask.FromResult<VoiceActivityTransferResultV1>(new VoiceActivityTransferResultV1.Rejected(
                    new VoiceActivitySourceOutcomeV1.Unavailable(
                        VoiceActivitySourceUnavailableReasonV1.ProviderUnavailable,
                        VoiceActivityRetryabilityV1.SameGeneration)));
            _accepted.Add(window.OperationId);
            return LoseTransferAcknowledgement
                ? ValueTask.FromException<VoiceActivityTransferResultV1>(new OperationCanceledException())
                : ValueTask.FromResult<VoiceActivityTransferResultV1>(
                    new VoiceActivityTransferResultV1.Accepted(window.OperationId));
        }

        public ValueTask<VoiceActivitySettlementResultV1> SettleAsync(
            OperationId operationId, CancellationToken cancellationToken)
        {
            if (!_accepted.Contains(operationId))
                return ValueTask.FromResult<VoiceActivitySettlementResultV1>(
                    new VoiceActivitySettlementResultV1.NotFound(operationId));
            return ValueTask.FromResult<VoiceActivitySettlementResultV1>(Settlement switch
            {
                SettlementMode.Pending => new VoiceActivitySettlementResultV1.Pending(operationId),
                SettlementMode.Unknown => new VoiceActivitySettlementResultV1.OutcomeUnknown(operationId),
                _ => new VoiceActivitySettlementResultV1.Settled(operationId,
                    new VoiceActivitySourceOutcomeV1.NoObservation(VoiceActivityNoObservationReasonV1.Gap)),
            });
        }
    }
}
