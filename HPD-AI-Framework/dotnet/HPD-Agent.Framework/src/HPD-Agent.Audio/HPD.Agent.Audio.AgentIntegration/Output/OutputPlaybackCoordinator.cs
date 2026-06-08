using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Trace;
using HPD.Events;
using HPD.Events.Struct;

namespace HPD.Agent.Audio.AgentIntegration.Output;

internal sealed class OutputPlaybackCoordinator
{
    private readonly OutputPlaybackCoordinatorOptions _options;
    private readonly IOutputFlow _flow;
    private readonly Dictionary<OutputSegmentId, OutputPlaybackRequest> _requests = [];
    private readonly HashSet<int> _completedSegmentIndexes = [];
    private readonly OutputLedgerTraceWriter _writer = new();
    private readonly List<RealtimeLedgerRecord> _ledger = [];
    private readonly List<RealtimeAudioTraceRecord> _trace = [];
    private readonly SequencedStructEventEmitter<AudioOutputPlayoutSample>? _playoutEmitter;
    private readonly SequencedStructEventEmitter<AudioOutputQueueDepthSample>? _queueDepthEmitter;
    private OutputPlaybackCompletedEvent? _pendingFinalCompleted;
    private long _playoutSampleTraceSequence;
    private long _queueDepthSampleTraceSequence;

    public OutputPlaybackCoordinator(
        OutputPlaybackCoordinatorOptions options,
        IOutputFlow flow)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _playoutEmitter = options.StructEvents?
            .Route<AudioOutputPlayoutSample>()
            .CreateSequencedEmitter();
        _queueDepthEmitter = options.StructEvents?
            .Route<AudioOutputQueueDepthSample>()
            .CreateSequencedEmitter();
    }

    public IReadOnlyList<RealtimeLedgerRecord> Ledger => _ledger;

    public IReadOnlyList<RealtimeAudioTraceRecord> Trace => _trace;

    public async ValueTask<OutputCommitRecord?> DrainPlaybackEventsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SeedPlaybackRequestsFromSnapshot();

        OutputCommitRecord? commit = null;
        await foreach (var playbackEvent in _options.Sink
            .ReadPlaybackEventsAsync(_flow.Id, cancellationToken)
            .ConfigureAwait(false))
        {
            commit = await ApplyPlaybackEventAsync(playbackEvent, cancellationToken)
                .ConfigureAwait(false) ?? commit;
        }

        if (commit is not null)
        {
            _options.EventFlowHandle?.Complete();
        }

        return commit;
    }

    public async ValueTask<OutputCommitRecord> InterruptAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _options.EventFlowHandle?.Interrupt();
        var boundary = await _options.Sink.InterruptAsync(_flow.Id, cancellationToken)
            .ConfigureAwait(false);
        var commit = await _flow.CommitInterruptedAsync(boundary, cancellationToken)
            .ConfigureAwait(false);
        EmitInterrupted(boundary);
        return commit;
    }

    internal async ValueTask<OutputCommitRecord?> ApplyPlaybackEventAsync(
        OutputPlaybackEvent playbackEvent,
        CancellationToken cancellationToken)
    {
        switch (playbackEvent)
        {
            case OutputPlaybackQueuedEvent queued:
                if (_requests.TryGetValue(queued.SegmentId, out var request))
                {
                    await _flow.MarkQueuedAsync(request, cancellationToken).ConfigureAwait(false);
                    AppendPlaybackEvidence(
                        request.ResponseId,
                        request.SegmentId,
                        request.SegmentIndex,
                        OutputPlaybackDisposition.Queued,
                        TimeSpan.Zero,
                        playedTextLength: 0,
                        OutputAlignmentPrecision.Unknown,
                        error: null);
                    EmitQueueDepthSample();
                    EmitQueued(request);
                }
                return null;

            case OutputPlaybackStartedEvent started:
                await _flow.MarkPlaybackStartedAsync(started, cancellationToken).ConfigureAwait(false);
                AppendPlaybackEvidence(
                    started.ResponseId,
                    started.SegmentId,
                    started.SegmentIndex,
                    OutputPlaybackDisposition.Started,
                    TimeSpan.Zero,
                    playedTextLength: 0,
                    OutputAlignmentPrecision.Unknown,
                    error: null);
                EmitStarted(started);
                return null;

            case OutputPlaybackProgressEvent progress:
                await _flow.UpdatePlaybackProgressAsync(progress.Cursor, cancellationToken).ConfigureAwait(false);
                AppendPlaybackEvidence(
                    progress.Cursor.ResponseId,
                    progress.Cursor.SegmentId,
                    progress.Cursor.SegmentIndex,
                    OutputPlaybackDisposition.Progress,
                    progress.Cursor.PlayedDuration,
                    progress.Cursor.PlayedTextLength,
                    progress.Cursor.Precision,
                    error: null);
                EmitProgress(progress.Cursor);
                return null;

            case OutputPlaybackCompletedEvent completed:
                return await HandleCompletedAsync(completed, cancellationToken).ConfigureAwait(false);

            case OutputPlaybackInterruptedEvent interrupted:
                var interruptedCommit = await _flow.CommitInterruptedAsync(interrupted.Boundary, cancellationToken)
                    .ConfigureAwait(false);
                AppendPlaybackEvidence(
                    interrupted.Boundary.ResponseId,
                    interrupted.Boundary.SegmentId,
                    interrupted.Boundary.SegmentIndex,
                    OutputPlaybackDisposition.Interrupted,
                    interrupted.Boundary.PlayedDuration,
                    interrupted.Boundary.PlayedTextLength,
                    interrupted.Boundary.Precision,
                    error: null);
                EmitInterrupted(interrupted.Boundary);
                return interruptedCommit;

            case OutputPlaybackClearedEvent cleared:
                if (IsFinalSegment(cleared.SegmentId))
                {
                    var queuedUnplayedCommit = await _flow.CommitQueuedUnplayedAsync(cleared.Boundary, cancellationToken)
                        .ConfigureAwait(false);
                    AppendPlaybackEvidence(
                        cleared.Boundary.ResponseId,
                        cleared.Boundary.SegmentId,
                        cleared.Boundary.SegmentIndex,
                        OutputPlaybackDisposition.QueuedUnplayed,
                        TimeSpan.Zero,
                        playedTextLength: 0,
                        cleared.Boundary.Precision,
                        error: null);
                    return queuedUnplayedCommit;
                }

                AppendPlaybackEvidence(
                    cleared.Boundary.ResponseId,
                    cleared.Boundary.SegmentId,
                    cleared.Boundary.SegmentIndex,
                    OutputPlaybackDisposition.QueuedUnplayed,
                    TimeSpan.Zero,
                    playedTextLength: 0,
                    cleared.Boundary.Precision,
                    error: null);
                return null;

            case OutputPlaybackFailedEvent failed:
                var failureCommit = await _flow.FailPlaybackAsync(new OutputPlaybackFailure
                {
                    OutputFlowId = failed.OutputFlowId,
                    ResponseId = failed.ResponseId,
                    SegmentId = failed.SegmentId,
                    SegmentIndex = failed.SegmentIndex,
                    Error = failed.Error,
                    ObservedAt = failed.ObservedAt
                }, cancellationToken).ConfigureAwait(false);
                AppendPlaybackEvidence(
                    failed.ResponseId,
                    failed.SegmentId,
                    failed.SegmentIndex,
                    OutputPlaybackDisposition.PlaybackFailed,
                    TimeSpan.Zero,
                    playedTextLength: 0,
                    OutputAlignmentPrecision.Unknown,
                    failed.Error);
                EmitFailed(failed);
                return failureCommit;

            default:
                return null;
        }
    }

    private OutputPlaybackRequest CreateRequest(OutputAudioStream stream, TimeSpan? estimatedDuration)
    {
        return new OutputPlaybackRequest
        {
            OutputFlowId = _flow.Id,
            ResponseId = stream.ResponseId,
            SegmentId = stream.SegmentId,
            SegmentIndex = stream.SegmentIndex,
            EstimatedDuration = estimatedDuration,
            EventFlowId = _options.EventFlowHandle?.EventFlowId,
            SourceTextStart = stream.SourceTextStart,
            SourceTextLength = stream.SourceTextLength,
            IsFinalSegment = stream.IsFinalSegment,
            MediaType = stream.MediaType,
            Alignment = stream.Alignment,
            Interruptibility = _options.Interruptibility
        };
    }

    private void SeedPlaybackRequestsFromSnapshot()
    {
        var snapshot = _flow.Snapshot;
        foreach (var stream in snapshot.AudioStreams.OrderBy(stream => stream.SegmentIndex))
        {
            if (_requests.ContainsKey(stream.SegmentId))
            {
                continue;
            }

            var request = CreateRequest(stream, EstimateDurationFromChunks(snapshot, stream.SegmentId));
            _requests.Add(request.SegmentId, request);
        }
    }

    private static TimeSpan? EstimateDurationFromChunks(OutputFlowSnapshot snapshot, OutputSegmentId segmentId)
    {
        var duration = snapshot.AudioChunks
            .Where(chunk => chunk.SegmentId == segmentId)
            .Select(chunk => chunk.Duration ?? TimeSpan.Zero)
            .Aggregate(TimeSpan.Zero, static (total, next) => total + next);
        return duration > TimeSpan.Zero ? duration : null;
    }

    private bool IsFinalSegment(OutputSegmentId segmentId)
    {
        return _requests.TryGetValue(segmentId, out var request) && request.IsFinalSegment;
    }

    private async ValueTask<OutputCommitRecord?> HandleCompletedAsync(
        OutputPlaybackCompletedEvent completed,
        CancellationToken cancellationToken)
    {
        if (IsFinalSegment(completed.SegmentId))
        {
            _pendingFinalCompleted = completed;
            return await TryCommitPendingFinalAsync(cancellationToken).ConfigureAwait(false);
        }

        await ApplyNonFinalCompletedAsync(completed, cancellationToken).ConfigureAwait(false);
        return await TryCommitPendingFinalAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ApplyNonFinalCompletedAsync(
        OutputPlaybackCompletedEvent completed,
        CancellationToken cancellationToken)
    {
        await _flow.UpdatePlaybackProgressAsync(completed.Cursor, cancellationToken).ConfigureAwait(false);
        _completedSegmentIndexes.Add(completed.SegmentIndex);
        AppendPlaybackEvidence(
            completed.Cursor.ResponseId,
            completed.Cursor.SegmentId,
            completed.Cursor.SegmentIndex,
            OutputPlaybackDisposition.Progress,
            completed.Cursor.PlayedDuration,
            completed.Cursor.PlayedTextLength,
            completed.Cursor.Precision,
            error: null);
        EmitProgress(completed.Cursor);
    }

    private async ValueTask<OutputCommitRecord?> TryCommitPendingFinalAsync(
        CancellationToken cancellationToken)
    {
        if (_pendingFinalCompleted is not { } completed ||
            !EarlierSegmentsCompleted(completed.SegmentIndex))
        {
            return null;
        }

        _pendingFinalCompleted = null;
        _completedSegmentIndexes.Add(completed.SegmentIndex);
        var commit = await _flow.CompletePlayedAsync(completed.Cursor, cancellationToken)
            .ConfigureAwait(false);
        AppendPlaybackEvidence(
            completed.Cursor.ResponseId,
            completed.Cursor.SegmentId,
            completed.Cursor.SegmentIndex,
            OutputPlaybackDisposition.PlayedComplete,
            completed.Cursor.PlayedDuration,
            completed.Cursor.PlayedTextLength,
            completed.Cursor.Precision,
            error: null);
        EmitCompleted(completed.Cursor);
        return commit;
    }

    private bool EarlierSegmentsCompleted(int finalSegmentIndex)
    {
        return _requests.Values
            .Where(request => request.SegmentIndex < finalSegmentIndex)
            .All(request => _completedSegmentIndexes.Contains(request.SegmentIndex));
    }

    private void AppendPlaybackEvidence(
        ResponseId responseId,
        OutputSegmentId? segmentId,
        int segmentIndex,
        OutputPlaybackDisposition disposition,
        TimeSpan playedDuration,
        int playedTextLength,
        OutputAlignmentPrecision precision,
        AudioErrorInfo? error)
    {
        _writer.AppendOutputPlayback(
            _ledger,
            _trace,
            _options.SessionId,
            CreateCorrelation(),
            _flow.Id,
            responseId,
            segmentId,
            segmentIndex,
            disposition,
            playedDuration,
            playedTextLength,
            precision,
            error);
        EmitPlayoutSample(
            segmentId,
            segmentIndex,
            playedDuration,
            playedTextLength,
            disposition);
    }

    private AudioCorrelation CreateCorrelation()
    {
        return new AudioCorrelation
        {
            SessionId = _options.SessionId,
            OutputFlowId = _flow.Id
        };
    }

    private void EmitPlayoutSample(
        OutputSegmentId? segmentId,
        int segmentIndex,
        TimeSpan playedDuration,
        int playedTextLength,
        OutputPlaybackDisposition disposition)
    {
        if (_playoutEmitter is null ||
            disposition is not (OutputPlaybackDisposition.Progress
                or OutputPlaybackDisposition.PlayedComplete
                or OutputPlaybackDisposition.Interrupted))
        {
            return;
        }

        var sample = new AudioOutputPlayoutSample(
            _options.SessionId.Value,
            _flow.Id.Value,
            segmentId?.Value,
            segmentIndex,
            ToNanoseconds(playedDuration),
            playedTextLength,
            CurrentUnixTimeNs());
        if (_playoutEmitter.Value.Emit(in sample).Accepted)
        {
            AppendStructSampleTrace(
                nameof(AudioOutputPlayoutSample),
                Interlocked.Increment(ref _playoutSampleTraceSequence),
                segmentId,
                segmentIndex);
        }
    }

    private void EmitQueueDepthSample()
    {
        if (_queueDepthEmitter is null)
        {
            return;
        }

        var queuedRequests = _requests.Values.ToArray();
        var queuedDuration = queuedRequests
            .Select(request => request.EstimatedDuration ?? TimeSpan.Zero)
            .Aggregate(TimeSpan.Zero, static (total, duration) => total + duration);

        var sample = new AudioOutputQueueDepthSample(
            _options.SessionId.Value,
            _flow.Id.Value,
            queuedRequests.Length,
            QueuedFrames: 0,
            ToNanoseconds(queuedDuration),
            CurrentUnixTimeNs());
        if (_queueDepthEmitter.Value.Emit(in sample).Accepted)
        {
            AppendStructSampleTrace(
                nameof(AudioOutputQueueDepthSample),
                Interlocked.Increment(ref _queueDepthSampleTraceSequence),
                segmentId: null,
                segmentIndex: 0);
        }
    }

    private void AppendStructSampleTrace(
        string structEventType,
        long sequenceNumber,
        OutputSegmentId? segmentId,
        int segmentIndex)
    {
        if (!_options.CaptureStructEventSamplesInTrace)
        {
            return;
        }

        _trace.Add(new AudioStructEventSampleTraceRecord
        {
            Id = new TraceRecordId($"trace-{Guid.NewGuid():N}"),
            SessionId = _options.SessionId,
            Family = RealtimeAudioTraceRecordFamily.StructEventSample,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = CreateCorrelation(),
            StructEventType = structEventType,
            SequenceNumber = sequenceNumber,
            OutputFlowId = _flow.Id,
            SegmentId = segmentId,
            SegmentIndex = segmentIndex
        });
    }

    private static long ToNanoseconds(TimeSpan duration)
    {
        return duration.Ticks * 100;
    }

    private static long CurrentUnixTimeNs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;
    }

    private void EmitQueued(OutputPlaybackRequest request)
    {
        _options.EmitEvent?.Invoke(WithFlow(new AssistantAudioPlaybackQueuedEvent(
            _options.SessionId.Value,
            _flow.Id.Value,
            request.ResponseId.Value,
            request.SegmentId.Value,
            request.SegmentIndex,
            request.MediaType ?? "application/octet-stream",
            Played: false,
            HeardByUser: false), canInterrupt: true));
    }

    private void EmitStarted(OutputPlaybackStartedEvent started)
    {
        if (!_requests.TryGetValue(started.SegmentId, out var request))
        {
            return;
        }

        _options.EmitEvent?.Invoke(WithFlow(new AssistantAudioPlaybackStartedEvent(
            _options.SessionId.Value,
            _flow.Id.Value,
            started.ResponseId.Value,
            started.SegmentId.Value,
            started.SegmentIndex,
            request.MediaType ?? "application/octet-stream"), canInterrupt: true));
    }

    private void EmitProgress(OutputPlaybackCursor cursor)
    {
        _options.EmitEvent?.Invoke(WithFlow(new AssistantAudioPlaybackProgressEvent(
            _options.SessionId.Value,
            _flow.Id.Value,
            cursor.ResponseId.Value,
            cursor.SegmentId?.Value,
            cursor.SegmentIndex,
            cursor.PlayedDuration,
            cursor.PlayedTextLength,
            cursor.Precision.ToString(),
            Played: false,
            HeardByUser: false), canInterrupt: true));
    }

    private void EmitCompleted(OutputPlaybackCursor cursor)
    {
        if (cursor.SegmentId is not { } segmentId ||
            !_requests.TryGetValue(segmentId, out var request))
        {
            return;
        }

        _options.EmitEvent?.Invoke(WithFlow(new AssistantAudioPlaybackCompletedEvent(
            _options.SessionId.Value,
            _flow.Id.Value,
            cursor.ResponseId.Value,
            segmentId.Value,
            cursor.SegmentIndex,
            request.MediaType ?? "application/octet-stream",
            Played: true,
            HeardByUser: true,
            cursor.PlayedDuration,
            cursor.PlayedTextLength,
            cursor.Precision.ToString()), canInterrupt: false));
    }

    private void EmitInterrupted(OutputPlaybackBoundary boundary)
    {
        _options.EmitEvent?.Invoke(WithFlow(new AssistantAudioPlaybackInterruptedEvent(
            _options.SessionId.Value,
            _flow.Id.Value,
            boundary.ResponseId.Value,
            boundary.SegmentId?.Value,
            boundary.SegmentIndex,
            boundary.PlayedDuration,
            boundary.PlayedTextLength,
            boundary.Precision.ToString(),
            Played: boundary.PlayedTextLength > 0,
            HeardByUser: boundary.PlayedTextLength > 0), canInterrupt: false));
    }

    private void EmitFailed(OutputPlaybackFailedEvent failed)
    {
        _options.EmitEvent?.Invoke(WithFlow(new AssistantAudioPlaybackFailedEvent(
            _options.SessionId.Value,
            _flow.Id.Value,
            failed.ResponseId.Value,
            failed.SegmentId.Value,
            failed.SegmentIndex,
            failed.Error,
            Played: false,
            HeardByUser: false), canInterrupt: false));
    }

    private TEvent WithFlow<TEvent>(TEvent evt, bool canInterrupt)
        where TEvent : AgentEvent
    {
        if (_options.EventFlowHandle is null)
        {
            return evt with { CanInterrupt = canInterrupt };
        }

        return evt with
        {
            EventFlowId = _options.EventFlowHandle.EventFlowId,
            CanInterrupt = canInterrupt
        };
    }
}

internal sealed record OutputPlaybackCoordinatorOptions
{
    public required AudioSessionId SessionId { get; init; }

    public required IAudioOutputSink Sink { get; init; }

    public IEventFlowHandle? EventFlowHandle { get; init; }

    public OutputInterruptibility Interruptibility { get; init; } = OutputInterruptibility.Interruptible;

    public Action<AgentEvent>? EmitEvent { get; init; }

    public IStructEventHub? StructEvents { get; init; }

    public bool CaptureStructEventSamplesInTrace { get; init; }
}
