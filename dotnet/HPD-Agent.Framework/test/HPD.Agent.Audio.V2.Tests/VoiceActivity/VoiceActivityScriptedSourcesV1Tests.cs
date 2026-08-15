using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityScriptedSourcesV1Tests
{
    [Fact]
    public void Synchronous_script_consumes_borrowed_window_without_receipt_or_retention()
    {
        var expected = new VoiceActivitySourceOutcomeV1.NoObservation(VoiceActivityNoObservationReasonV1.Gap);
        var source = new ScriptedSyncSource(SyncCapabilities(), expected);
        var bytes = new byte[] { 1, 2, 3, 4 };
        var window = new VoiceActivityBorrowedWindowV1(bytes, DecodedFormat(), Extent(), Stamp(1));

        Assert.Same(expected, source.Observe(in window));
        bytes[0] = 9;

        Assert.Equal(1, source.CallCount);
        Assert.Equal(10, source.LastChecksum);
        Assert.DoesNotContain(source.GetType().GetFields(), static field => field.FieldType == typeof(byte[]));
    }

    [Fact]
    public async Task Async_transfer_owns_bytes_and_settles_by_operation_identity()
    {
        var source = new ScriptedTransferredSource(AsyncCapabilities());
        var operation = OperationId.Create();
        var bytes = new byte[] { 4, 3, 2, 1 };
        var owned = new VoiceActivityOwnedWindowV1(operation, bytes, DecodedFormat(), Extent(), Stamp(1));

        var accepted = Assert.IsType<VoiceActivityTransferResultV1.Accepted>(
            await source.TransferAsync(owned, CancellationToken.None));
        bytes[0] = 99;
        source.Complete(operation, new VoiceActivitySourceOutcomeV1.NoObservation(VoiceActivityNoObservationReasonV1.Timeout));
        var settled = Assert.IsType<VoiceActivitySettlementResultV1.Settled>(
            await source.SettleAsync(accepted.OperationId, CancellationToken.None));

        Assert.Equal(10, source.AcceptedChecksum);
        Assert.Equal(operation, settled.OperationId);
        Assert.IsType<VoiceActivitySourceOutcomeV1.NoObservation>(settled.Outcome);
    }

    [Fact]
    public async Task Post_acceptance_cancellation_is_outcome_unknown_and_recoverable_by_settlement()
    {
        var source = new ScriptedTransferredSource(AsyncCapabilities()) { LoseAcceptanceAcknowledgement = true };
        var operation = OperationId.Create();
        var owned = new VoiceActivityOwnedWindowV1(operation, new byte[] { 1 }, DecodedFormat(), Extent(), Stamp(1));

        var unknown = Assert.IsType<VoiceActivityTransferResultV1.OutcomeUnknown>(
            await source.TransferAsync(owned, CancellationToken.None));
        Assert.Equal(operation, unknown.OperationId);
        Assert.IsType<VoiceActivitySettlementResultV1.Pending>(
            await source.SettleAsync(operation, CancellationToken.None));

        source.Complete(operation, new VoiceActivitySourceOutcomeV1.Unavailable(
            VoiceActivitySourceUnavailableReasonV1.CapacityUnavailable, VoiceActivityRetryabilityV1.SameGeneration));
        Assert.IsType<VoiceActivitySettlementResultV1.Settled>(
            await source.SettleAsync(operation, CancellationToken.None));
    }

    [Fact]
    public async Task Pre_acceptance_cancellation_does_not_create_owned_work()
    {
        var source = new ScriptedTransferredSource(AsyncCapabilities());
        var operation = OperationId.Create();
        var owned = new VoiceActivityOwnedWindowV1(operation, new byte[] { 1 }, DecodedFormat(), Extent(), Stamp(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await source.TransferAsync(owned, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.IsType<VoiceActivitySettlementResultV1.NotFound>(
            await source.SettleAsync(operation, CancellationToken.None));
    }

    [Fact]
    public async Task Provider_opaque_stt_adjacent_and_manual_scripts_keep_semantic_outcomes_distinct()
    {
        var opaque = new ScriptedTransferredSource(OpaqueCapabilities());
        var operation = OperationId.Create();
        var owned = new VoiceActivityOwnedWindowV1(operation, new byte[] { 7 },
            new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.ProviderOpaque, 0, 0), Extent(), Stamp(1));
        Assert.IsType<VoiceActivityTransferResultV1.Accepted>(await opaque.TransferAsync(owned, CancellationToken.None));

        var sttAdjacent = new VoiceActivitySourceOutcomeV1.NoObservation(VoiceActivityNoObservationReasonV1.ProviderNotObservable);
        var manual = new VoiceActivitySourceOutcomeV1.Observed(
            new VoiceActivityMeasurementV1.Category(new BoundedAscii("manual-pressed")),
            new VoiceActivityMeasurementDescriptorV1(VoiceActivityMeasurementKindV1.PostProcessedState,
                new BoundedAscii("manual"), 0, 1, null), Extent(), 1, Stamp(1), Stamp(1));

        Assert.IsType<VoiceActivitySourceOutcomeV1.NoObservation>(sttAdjacent);
        Assert.Equal("manual-pressed", Assert.IsType<VoiceActivityMeasurementV1.Category>(manual.Measurement).Value.ToString());
    }

    private sealed class ScriptedSyncSource : IBorrowedSynchronousVoiceActivitySourceV1
    {
        private readonly VoiceActivitySourceOutcomeV1 _outcome;
        internal ScriptedSyncSource(VoiceActivitySourceCapabilitiesV1 capabilities, VoiceActivitySourceOutcomeV1 outcome) =>
            (Capabilities, _outcome) = (capabilities, outcome);
        public VoiceActivitySourceCapabilitiesV1 Capabilities { get; }
        internal int CallCount { get; private set; }
        internal int LastChecksum { get; private set; }
        public VoiceActivitySourceOutcomeV1 Observe(scoped in VoiceActivityBorrowedWindowV1 window)
        {
            CallCount++;
            foreach (var value in window.Bytes) LastChecksum += value;
            return _outcome;
        }
    }

    private sealed class ScriptedTransferredSource : ITransferredVoiceActivitySourceV1
    {
        private readonly Dictionary<OperationId, VoiceActivitySourceOutcomeV1?> _operations = [];
        internal ScriptedTransferredSource(VoiceActivitySourceCapabilitiesV1 capabilities) => Capabilities = capabilities;
        public VoiceActivitySourceCapabilitiesV1 Capabilities { get; }
        internal bool LoseAcceptanceAcknowledgement { get; init; }
        internal int AcceptedChecksum { get; private set; }
        public ValueTask<VoiceActivityTransferResultV1> TransferAsync(VoiceActivityOwnedWindowV1 window, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_operations.ContainsKey(window.OperationId))
                return ValueTask.FromResult<VoiceActivityTransferResultV1>(new VoiceActivityTransferResultV1.OutcomeUnknown(window.OperationId));
            AcceptedChecksum = window.Bytes.Span.ToArray().Sum(static value => value);
            _operations.Add(window.OperationId, null);
            return ValueTask.FromResult<VoiceActivityTransferResultV1>(LoseAcceptanceAcknowledgement
                ? new VoiceActivityTransferResultV1.OutcomeUnknown(window.OperationId)
                : new VoiceActivityTransferResultV1.Accepted(window.OperationId));
        }
        public ValueTask<VoiceActivitySettlementResultV1> SettleAsync(OperationId operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_operations.TryGetValue(operationId, out var outcome))
                return ValueTask.FromResult<VoiceActivitySettlementResultV1>(new VoiceActivitySettlementResultV1.NotFound(operationId));
            return ValueTask.FromResult<VoiceActivitySettlementResultV1>(outcome is null
                ? new VoiceActivitySettlementResultV1.Pending(operationId)
                : new VoiceActivitySettlementResultV1.Settled(operationId, outcome));
        }
        internal void Complete(OperationId operationId, VoiceActivitySourceOutcomeV1 outcome) => _operations[operationId] = outcome;
    }

    private static VoiceActivitySourceCapabilitiesV1 SyncCapabilities() => Capabilities(
        VoiceActivityInputOwnershipV1.BorrowedSynchronous,
        new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1),
        VoiceActivitySourceConcurrencyV1.Serial, VoiceActivitySourceControlV1.Unsupported, 1);
    private static VoiceActivitySourceCapabilitiesV1 AsyncCapabilities() => Capabilities(
        VoiceActivityInputOwnershipV1.IsolatedTransferred, DecodedFormat(),
        VoiceActivitySourceConcurrencyV1.ParallelWindows, VoiceActivitySourceControlV1.Sequenced, 4);
    private static VoiceActivitySourceCapabilitiesV1 OpaqueCapabilities() => Capabilities(
        VoiceActivityInputOwnershipV1.ProviderOpaque,
        new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.ProviderOpaque, 0, 0),
        VoiceActivitySourceConcurrencyV1.ProviderManaged, VoiceActivitySourceControlV1.Sequenced, 4);
    private static VoiceActivitySourceCapabilitiesV1 Capabilities(VoiceActivityInputOwnershipV1 ownership,
        VoiceActivityInputFormatV1 format, VoiceActivitySourceConcurrencyV1 concurrency,
        VoiceActivitySourceControlV1 transfer, int pending) => new(
            ownership, new[] { format },
            new VoiceActivityWindowCapabilityV1(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10), 8),
            new VoiceActivityMeasurementDescriptorV1(VoiceActivityMeasurementKindV1.EngineScore,
                new BoundedAscii("score"), -1, 1, null),
            ownership == VoiceActivityInputOwnershipV1.ProviderOpaque
                ? VoiceActivitySourceStateModelV1.ProviderOpaque : VoiceActivitySourceStateModelV1.GenerationLocal,
            concurrency, VoiceActivitySourceControlV1.Sequenced, VoiceActivitySourceControlV1.Sequenced,
            transfer, VoiceActivitySourceControlV1.ReplacementRequired, true, true, pending);
    private static VoiceActivityInputFormatV1 DecodedFormat() =>
        new(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1);
    private static VoiceActivityMediaExtentV1 Extent() => new(GraphGenerationId.Create(), 0, 10, true);
    private static readonly ClockDomainId Domain = ClockDomainId.Create();
    private static readonly BootId Boot = BootId.Create();
    private static MonotonicStampV1 Stamp(ulong value) => new(Domain, Boot, value);
}
