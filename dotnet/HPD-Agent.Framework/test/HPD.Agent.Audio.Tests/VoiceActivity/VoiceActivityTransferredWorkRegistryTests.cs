using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityTransferredWorkRegistryTests
{
    [Fact]
    public async Task Capacity_and_duplicate_execution_are_bounded_until_terminal_settlement()
    {
        var source = new Source(Capabilities(1));
        var registry = new VoiceActivityTransferredWorkRegistry(source);
        var first = Window(OperationId.Create());

        Assert.IsType<VoiceActivityTransferResult.Accepted>(await registry.TransferAsync(first, default));
        Assert.IsType<VoiceActivityTransferResult.OutcomeUnknown>(await registry.TransferAsync(first, default));
        var saturated = Assert.IsType<VoiceActivityTransferResult.Rejected>(
            await registry.TransferAsync(Window(OperationId.Create()), default));
        Assert.Equal(VoiceActivitySourceUnavailableReason.CapacityUnavailable,
            Assert.IsType<VoiceActivitySourceOutcome.Unavailable>(saturated.Outcome).Reason);
        Assert.Equal(1, source.TransferCalls);
        Assert.Equal(1, registry.PendingCount);

        Assert.IsType<VoiceActivitySettlementResult.Settled>(
            await registry.SettleAsync(first.OperationId, default));
        Assert.Equal(0, registry.PendingCount);
        Assert.IsType<VoiceActivityTransferResult.Accepted>(
            await registry.TransferAsync(Window(OperationId.Create()), default));
    }

    [Fact]
    public async Task Lost_acceptance_acknowledgement_retains_credit_until_queryable_settlement()
    {
        var source = new Source(Capabilities(1)) { LoseTransferAcknowledgement = true };
        var registry = new VoiceActivityTransferredWorkRegistry(source);
        var window = Window(OperationId.Create());

        Assert.IsType<VoiceActivityTransferResult.OutcomeUnknown>(
            await registry.TransferAsync(window, default));
        Assert.Equal(1, registry.PendingCount);
        Assert.IsType<VoiceActivitySettlementResult.Settled>(
            await registry.SettleAsync(window.OperationId, default));
        Assert.Equal(0, registry.PendingCount);
    }

    [Fact]
    public async Task Rejection_releases_credit_but_pending_and_unknown_settlement_do_not()
    {
        var source = new Source(Capabilities(1)) { RejectTransfer = true };
        var registry = new VoiceActivityTransferredWorkRegistry(source);
        Assert.IsType<VoiceActivityTransferResult.Rejected>(
            await registry.TransferAsync(Window(OperationId.Create()), default));
        Assert.Equal(0, registry.PendingCount);

        source.RejectTransfer = false;
        source.Settlement = SettlementMode.Pending;
        var window = Window(OperationId.Create());
        await registry.TransferAsync(window, default);
        Assert.IsType<VoiceActivitySettlementResult.Pending>(
            await registry.SettleAsync(window.OperationId, default));
        Assert.Equal(1, registry.PendingCount);
        source.Settlement = SettlementMode.Unknown;
        Assert.IsType<VoiceActivitySettlementResult.OutcomeUnknown>(
            await registry.SettleAsync(window.OperationId, default));
        Assert.Equal(1, registry.PendingCount);
    }

    [Fact]
    public async Task Close_fences_new_work_but_preserves_pending_settlement()
    {
        var source = new Source(Capabilities(1));
        var registry = new VoiceActivityTransferredWorkRegistry(source);
        var pending = Window(OperationId.Create());
        await registry.TransferAsync(pending, default);
        registry.Close();

        var rejected = Assert.IsType<VoiceActivityTransferResult.Rejected>(
            await registry.TransferAsync(Window(OperationId.Create()), default));
        Assert.Equal(VoiceActivityNoObservationReason.SourceRevoked,
            Assert.IsType<VoiceActivitySourceOutcome.NoObservation>(rejected.Outcome).Reason);
        Assert.IsType<VoiceActivitySettlementResult.Settled>(
            await registry.SettleAsync(pending.OperationId, default));
        Assert.Equal(0, registry.PendingCount);
    }

    private static VoiceActivityOwnedWindow Window(OperationId operation) => new(
        operation, new byte[320],
        new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1),
        new VoiceActivityMediaExtent(GraphGenerationId.Create(), 1, 2, true),
        new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 1));

    private static VoiceActivitySourceCapabilities Capabilities(int maximumPending) => new(
        VoiceActivityInputOwnership.IsolatedTransferred,
        [new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1)],
        new VoiceActivityWindowCapability(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10), 1),
        new VoiceActivityMeasurementDescriptor(VoiceActivityMeasurementKind.EngineScore,
            new BoundedAscii("measurement"), -1, 1, null),
        VoiceActivitySourceStateModel.StreamLocal, VoiceActivitySourceConcurrency.ParallelWindows,
        VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.Sequenced,
        VoiceActivitySourceControl.Sequenced, VoiceActivitySourceControl.ReplacementRequired,
        true, false, maximumPending);

    private enum SettlementMode { Settled, Pending, Unknown }

    private sealed class Source(VoiceActivitySourceCapabilities capabilities) : ITransferredVoiceActivitySource
    {
        private readonly HashSet<OperationId> _accepted = [];
        public VoiceActivitySourceCapabilities Capabilities { get; } = capabilities;
        public int TransferCalls { get; private set; }
        public bool LoseTransferAcknowledgement { get; set; }
        public bool RejectTransfer { get; set; }
        public SettlementMode Settlement { get; set; }

        public ValueTask<VoiceActivityTransferResult> TransferAsync(
            VoiceActivityOwnedWindow window, CancellationToken cancellationToken)
        {
            TransferCalls++;
            if (RejectTransfer)
                return ValueTask.FromResult<VoiceActivityTransferResult>(new VoiceActivityTransferResult.Rejected(
                    new VoiceActivitySourceOutcome.Unavailable(
                        VoiceActivitySourceUnavailableReason.ProviderUnavailable,
                        VoiceActivityRetryability.SameGeneration)));
            _accepted.Add(window.OperationId);
            return LoseTransferAcknowledgement
                ? ValueTask.FromException<VoiceActivityTransferResult>(new OperationCanceledException())
                : ValueTask.FromResult<VoiceActivityTransferResult>(
                    new VoiceActivityTransferResult.Accepted(window.OperationId));
        }

        public ValueTask<VoiceActivitySettlementResult> SettleAsync(
            OperationId operationId, CancellationToken cancellationToken)
        {
            if (!_accepted.Contains(operationId))
                return ValueTask.FromResult<VoiceActivitySettlementResult>(
                    new VoiceActivitySettlementResult.NotFound(operationId));
            return ValueTask.FromResult<VoiceActivitySettlementResult>(Settlement switch
            {
                SettlementMode.Pending => new VoiceActivitySettlementResult.Pending(operationId),
                SettlementMode.Unknown => new VoiceActivitySettlementResult.OutcomeUnknown(operationId),
                _ => new VoiceActivitySettlementResult.Settled(operationId,
                    new VoiceActivitySourceOutcome.NoObservation(VoiceActivityNoObservationReason.Gap)),
            });
        }
    }
}
