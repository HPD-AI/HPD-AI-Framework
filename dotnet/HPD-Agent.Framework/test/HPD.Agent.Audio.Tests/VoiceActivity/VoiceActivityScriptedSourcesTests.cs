using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityScriptedSourcesTests
{
    [Fact]
    public void Synchronous_script_consumes_borrowed_window_without_receipt_or_retention()
    {
        var expected = new VoiceActivitySourceOutcome.NoObservation(VoiceActivityNoObservationReason.Gap);
        var source = new ScriptedSyncSource(SyncCapabilities(), expected);
        var bytes = new byte[] { 1, 2, 3, 4 };
        var window = new VoiceActivityBorrowedWindow(bytes, DecodedFormat(), Extent(), Stamp(1));

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
        var owned = new VoiceActivityOwnedWindow(operation, bytes, DecodedFormat(), Extent(), Stamp(1));

        var accepted = Assert.IsType<VoiceActivityTransferResult.Accepted>(
            await source.TransferAsync(owned, CancellationToken.None));
        bytes[0] = 99;
        source.Complete(operation, new VoiceActivitySourceOutcome.NoObservation(VoiceActivityNoObservationReason.Timeout));
        var settled = Assert.IsType<VoiceActivitySettlementResult.Settled>(
            await source.SettleAsync(accepted.OperationId, CancellationToken.None));

        Assert.Equal(10, source.AcceptedChecksum);
        Assert.Equal(operation, settled.OperationId);
        Assert.IsType<VoiceActivitySourceOutcome.NoObservation>(settled.Outcome);
    }

    [Fact]
    public async Task Post_acceptance_cancellation_is_outcome_unknown_and_recoverable_by_settlement()
    {
        var source = new ScriptedTransferredSource(AsyncCapabilities()) { LoseAcceptanceAcknowledgement = true };
        var operation = OperationId.Create();
        var owned = new VoiceActivityOwnedWindow(operation, new byte[] { 1 }, DecodedFormat(), Extent(), Stamp(1));

        var unknown = Assert.IsType<VoiceActivityTransferResult.OutcomeUnknown>(
            await source.TransferAsync(owned, CancellationToken.None));
        Assert.Equal(operation, unknown.OperationId);
        Assert.IsType<VoiceActivitySettlementResult.Pending>(
            await source.SettleAsync(operation, CancellationToken.None));

        source.Complete(operation, new VoiceActivitySourceOutcome.Unavailable(
            VoiceActivitySourceUnavailableReason.CapacityUnavailable, VoiceActivityRetryability.SameGeneration));
        Assert.IsType<VoiceActivitySettlementResult.Settled>(
            await source.SettleAsync(operation, CancellationToken.None));
    }

    [Fact]
    public async Task Pre_acceptance_cancellation_does_not_create_owned_work()
    {
        var source = new ScriptedTransferredSource(AsyncCapabilities());
        var operation = OperationId.Create();
        var owned = new VoiceActivityOwnedWindow(operation, new byte[] { 1 }, DecodedFormat(), Extent(), Stamp(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await source.TransferAsync(owned, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.IsType<VoiceActivitySettlementResult.NotFound>(
            await source.SettleAsync(operation, CancellationToken.None));
    }

    [Fact]
    public async Task Provider_opaque_stt_adjacent_and_manual_scripts_keep_semantic_outcomes_distinct()
    {
        var opaque = new ScriptedTransferredSource(OpaqueCapabilities());
        var operation = OperationId.Create();
        var owned = new VoiceActivityOwnedWindow(operation, new byte[] { 7 },
            new VoiceActivityInputFormat(VoiceActivitySampleEncoding.ProviderOpaque, 0, 0), Extent(), Stamp(1));
        Assert.IsType<VoiceActivityTransferResult.Accepted>(await opaque.TransferAsync(owned, CancellationToken.None));

        var sttAdjacentOutcome = new VoiceActivitySourceOutcome.NoObservation(VoiceActivityNoObservationReason.ProviderNotObservable);
        var manualOutcome = new VoiceActivitySourceOutcome.Observed(
            new VoiceActivityMeasurement.Category(new BoundedAscii("manual-pressed")),
            new VoiceActivityMeasurementDescriptor(VoiceActivityMeasurementKind.PostProcessedState,
                new BoundedAscii("manual"), 0, 1, null), Extent(), 1, Stamp(1), Stamp(1));
        var bytes = new byte[] { 1 };
        var borrowed = new VoiceActivityBorrowedWindow(bytes, DecodedFormat(), Extent(), Stamp(1));
        var sttAdjacent = new ScriptedSyncSource(SyncCapabilities(), sttAdjacentOutcome).Observe(in borrowed);
        var manual = new ScriptedSyncSource(SyncCapabilities(), manualOutcome).Observe(in borrowed);

        Assert.IsType<VoiceActivitySourceOutcome.NoObservation>(sttAdjacent);
        Assert.Equal("manual-pressed", Assert.IsType<VoiceActivityMeasurement.Category>(
            Assert.IsType<VoiceActivitySourceOutcome.Observed>(manual).Measurement).Value.ToString());
    }

    [Fact]
    public async Task Injected_manual_clock_and_scheduler_make_async_processing_deterministic()
    {
        var clock = new ManualClock(Stamp(10));
        var scheduler = new ControlledScheduler();
        var source = new ScheduledScriptedSource(AsyncCapabilities(), clock, scheduler);
        var operation = OperationId.Create();
        var owned = new VoiceActivityOwnedWindow(operation, new byte[] { 2 }, DecodedFormat(), Extent(), Stamp(9));

        var transfer = source.TransferAsync(owned, CancellationToken.None);
        Assert.False(transfer.IsCompleted);
        clock.Advance(Stamp(11));
        scheduler.Release(operation);

        Assert.IsType<VoiceActivityTransferResult.Accepted>(await transfer);
        var settled = Assert.IsType<VoiceActivitySettlementResult.Settled>(
            await source.SettleAsync(operation, CancellationToken.None));
        var observed = Assert.IsType<VoiceActivitySourceOutcome.Observed>(settled.Outcome);
        Assert.Equal(Stamp(11), observed.ProcessedAt);
        Assert.Equal(new[] { operation }, scheduler.Admitted);
    }

    private sealed class ScriptedSyncSource : IBorrowedSynchronousVoiceActivitySource
    {
        private readonly VoiceActivitySourceOutcome _outcome;
        internal ScriptedSyncSource(VoiceActivitySourceCapabilities capabilities, VoiceActivitySourceOutcome outcome) =>
            (Capabilities, _outcome) = (capabilities, outcome);
        public VoiceActivitySourceCapabilities Capabilities { get; }
        internal int CallCount { get; private set; }
        internal int LastChecksum { get; private set; }
        public VoiceActivitySourceOutcome Observe(scoped in VoiceActivityBorrowedWindow window)
        {
            CallCount++;
            foreach (var value in window.Bytes) LastChecksum += value;
            return _outcome;
        }
    }

    private sealed class ScriptedTransferredSource : ITransferredVoiceActivitySource
    {
        private readonly Dictionary<OperationId, VoiceActivitySourceOutcome?> _operations = [];
        internal ScriptedTransferredSource(VoiceActivitySourceCapabilities capabilities) => Capabilities = capabilities;
        public VoiceActivitySourceCapabilities Capabilities { get; }
        internal bool LoseAcceptanceAcknowledgement { get; init; }
        internal int AcceptedChecksum { get; private set; }
        public ValueTask<VoiceActivityTransferResult> TransferAsync(VoiceActivityOwnedWindow window, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_operations.ContainsKey(window.OperationId))
                return ValueTask.FromResult<VoiceActivityTransferResult>(new VoiceActivityTransferResult.OutcomeUnknown(window.OperationId));
            AcceptedChecksum = window.Bytes.Span.ToArray().Sum(static value => value);
            _operations.Add(window.OperationId, null);
            return ValueTask.FromResult<VoiceActivityTransferResult>(LoseAcceptanceAcknowledgement
                ? new VoiceActivityTransferResult.OutcomeUnknown(window.OperationId)
                : new VoiceActivityTransferResult.Accepted(window.OperationId));
        }
        public ValueTask<VoiceActivitySettlementResult> SettleAsync(OperationId operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_operations.TryGetValue(operationId, out var outcome))
                return ValueTask.FromResult<VoiceActivitySettlementResult>(new VoiceActivitySettlementResult.NotFound(operationId));
            return ValueTask.FromResult<VoiceActivitySettlementResult>(outcome is null
                ? new VoiceActivitySettlementResult.Pending(operationId)
                : new VoiceActivitySettlementResult.Settled(operationId, outcome));
        }
        internal void Complete(OperationId operationId, VoiceActivitySourceOutcome outcome) => _operations[operationId] = outcome;
    }

    private sealed class ScheduledScriptedSource : ITransferredVoiceActivitySource
    {
        private readonly ManualClock _clock;
        private readonly ControlledScheduler _scheduler;
        private readonly Dictionary<OperationId, VoiceActivitySourceOutcome> _settled = [];
        internal ScheduledScriptedSource(VoiceActivitySourceCapabilities capabilities,
            ManualClock clock, ControlledScheduler scheduler) =>
            (Capabilities, _clock, _scheduler) = (capabilities, clock, scheduler);
        public VoiceActivitySourceCapabilities Capabilities { get; }
        public async ValueTask<VoiceActivityTransferResult> TransferAsync(
            VoiceActivityOwnedWindow window, CancellationToken cancellationToken)
        {
            await _scheduler.WaitAsync(window.OperationId, cancellationToken);
            _settled.Add(window.OperationId, new VoiceActivitySourceOutcome.Observed(
                new VoiceActivityMeasurement.Numeric(.5),
                new VoiceActivityMeasurementDescriptor(VoiceActivityMeasurementKind.EngineScore,
                    new BoundedAscii("score"), -1, 1, null),
                window.Extent, 1, window.ObservedAt, _clock.Now));
            return new VoiceActivityTransferResult.Accepted(window.OperationId);
        }
        public ValueTask<VoiceActivitySettlementResult> SettleAsync(
            OperationId operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<VoiceActivitySettlementResult>(_settled.TryGetValue(operationId, out var outcome)
                ? new VoiceActivitySettlementResult.Settled(operationId, outcome)
                : new VoiceActivitySettlementResult.NotFound(operationId));
        }
    }

    private sealed class ManualClock
    {
        internal ManualClock(MonotonicStampV1 now) => Now = now;
        internal MonotonicStampV1 Now { get; private set; }
        internal void Advance(MonotonicStampV1 now)
        {
            if (now.CompareTo(Now) != ClockComparison.Later)
                throw new ArgumentException("The manual clock must advance.", nameof(now));
            Now = now;
        }
    }

    private sealed class ControlledScheduler
    {
        private readonly Dictionary<OperationId, TaskCompletionSource> _gates = [];
        private readonly List<OperationId> _admitted = [];
        internal IReadOnlyList<OperationId> Admitted => _admitted;
        internal ValueTask WaitAsync(OperationId operationId, CancellationToken cancellationToken)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _gates.Add(operationId, gate);
            _admitted.Add(operationId);
            return new ValueTask(gate.Task.WaitAsync(cancellationToken));
        }
        internal void Release(OperationId operationId) => _gates[operationId].SetResult();
    }

    private static VoiceActivitySourceCapabilities SyncCapabilities() => Capabilities(
        VoiceActivityInputOwnership.BorrowedSynchronous,
        new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1),
        VoiceActivitySourceConcurrency.Serial, VoiceActivitySourceControl.Unsupported, 1);
    private static VoiceActivitySourceCapabilities AsyncCapabilities() => Capabilities(
        VoiceActivityInputOwnership.IsolatedTransferred, DecodedFormat(),
        VoiceActivitySourceConcurrency.ParallelWindows, VoiceActivitySourceControl.Sequenced, 4);
    private static VoiceActivitySourceCapabilities OpaqueCapabilities() => Capabilities(
        VoiceActivityInputOwnership.ProviderOpaque,
        new VoiceActivityInputFormat(VoiceActivitySampleEncoding.ProviderOpaque, 0, 0),
        VoiceActivitySourceConcurrency.ProviderManaged, VoiceActivitySourceControl.Sequenced, 4);
    private static VoiceActivitySourceCapabilities Capabilities(VoiceActivityInputOwnership ownership,
        VoiceActivityInputFormat format, VoiceActivitySourceConcurrency concurrency,
        VoiceActivitySourceControl transfer, int pending) => new(
            ownership, new[] { format },
            new VoiceActivityWindowCapability(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10), 8),
            new VoiceActivityMeasurementDescriptor(VoiceActivityMeasurementKind.EngineScore,
                new BoundedAscii("score"), -1, 1, null),
            ownership == VoiceActivityInputOwnership.ProviderOpaque
                ? VoiceActivitySourceStateModel.ProviderOpaque : VoiceActivitySourceStateModel.GenerationLocal,
            concurrency, VoiceActivitySourceControl.Sequenced, VoiceActivitySourceControl.Sequenced,
            transfer, VoiceActivitySourceControl.ReplacementRequired, true, true, pending);
    private static VoiceActivityInputFormat DecodedFormat() =>
        new(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1);
    private static VoiceActivityMediaExtent Extent() => new(GraphGenerationId.Create(), 0, 10, true);
    private static readonly ClockDomainId Domain = ClockDomainId.Create();
    private static readonly BootId Boot = BootId.Create();
    private static MonotonicStampV1 Stamp(ulong value) => new(Domain, Boot, value);
}
