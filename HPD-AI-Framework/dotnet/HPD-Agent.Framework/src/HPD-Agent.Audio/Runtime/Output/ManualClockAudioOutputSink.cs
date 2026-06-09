using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.Runtime.Output;

public sealed class ManualClockAudioOutputSink : IAudioOutputSink
{
    private readonly object _gate = new();
    private readonly Dictionary<OutputFlowId, FlowState> _flows = [];
    private readonly TimeSpan _defaultDuration;
    private readonly int _pcmPipeCapacity;
    private readonly bool _acceptEncodedAudio;
    private DateTimeOffset _observedAt;

    public ManualClockAudioOutputSink(
        DateTimeOffset? startsAt = null,
        TimeSpan? defaultDuration = null,
        int pcmPipeCapacity = 256,
        bool acceptEncodedAudio = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pcmPipeCapacity);
        _observedAt = startsAt ?? DateTimeOffset.UnixEpoch;
        _defaultDuration = defaultDuration ?? TimeSpan.FromSeconds(1);
        _pcmPipeCapacity = pcmPipeCapacity;
        _acceptEncodedAudio = acceptEncodedAudio;
    }

    public ValueTask<OutputSinkStartResult> StartAsync(
        OutputAudioStream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        var request = new OutputPlaybackRequest
        {
            OutputFlowId = stream.OutputFlowId,
            ResponseId = stream.ResponseId,
            SegmentId = stream.SegmentId,
            SegmentIndex = stream.SegmentIndex,
            SourceTextStart = stream.SourceTextStart,
            SourceTextLength = stream.SourceTextLength,
            IsFinalSegment = stream.IsFinalSegment,
            MediaType = stream.MediaType,
            Alignment = stream.Alignment,
            Interruptibility = stream.Interruptibility
        };

        var error = ValidateStart(stream);

        lock (_gate)
        {
            var flow = GetOrCreateFlowLocked(request.OutputFlowId, request.ResponseId);
            if (error is not null)
            {
                flow.RejectedSegments.Add(request.SegmentId);
                flow.Events.Enqueue(new OutputPlaybackFailedEvent
                {
                    OutputFlowId = request.OutputFlowId,
                    ResponseId = request.ResponseId,
                    SegmentId = request.SegmentId,
                    SegmentIndex = request.SegmentIndex,
                    Error = error,
                    ObservedAt = _observedAt
                });

                return ValueTask.FromResult(new OutputSinkStartResult
                {
                    OutputFlowId = request.OutputFlowId,
                    ResponseId = request.ResponseId,
                    SegmentId = request.SegmentId,
                    SegmentIndex = request.SegmentIndex,
                    Disposition = OutputSinkStartDisposition.Rejected,
                    Error = error
                });
            }

            flow.Entries.Add(new PlaybackEntry(request, ResolveDuration(request)));
            flow.Events.Enqueue(new OutputPlaybackQueuedEvent
            {
                OutputFlowId = request.OutputFlowId,
                ResponseId = request.ResponseId,
                SegmentId = request.SegmentId,
                SegmentIndex = request.SegmentIndex,
                ObservedAt = _observedAt
            });
        }

        return ValueTask.FromResult(new OutputSinkStartResult
        {
            OutputFlowId = request.OutputFlowId,
            ResponseId = request.ResponseId,
            SegmentId = request.SegmentId,
            SegmentIndex = request.SegmentIndex,
            Disposition = OutputSinkStartDisposition.Accepted
        });
    }

    public ValueTask WriteAsync(
        OutputAudioChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_flows.TryGetValue(chunk.OutputFlowId, out var flow) ||
                flow.RejectedSegments.Contains(chunk.SegmentId))
            {
                return ValueTask.CompletedTask;
            }

            switch (chunk.Payload)
            {
                case DecodedOutputAudioFrame decoded:
                    WriteDecodedFrameLocked(flow, chunk, decoded.Frame);
                    break;

                case EncodedOutputAudioData when _acceptEncodedAudio:
                    break;

                default:
                    RejectSegmentLocked(
                        flow,
                        chunk.OutputFlowId,
                        chunk.ResponseId,
                        chunk.SegmentId,
                        chunk.SegmentIndex,
                        UnsupportedPayloadError(chunk.Payload.Kind, chunk.MediaType));
                    break;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CompleteAsync(
        OutputAudioStreamCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();

        if (completion.Duration is { } duration && duration > TimeSpan.Zero)
        {
            lock (_gate)
            {
                var flow = GetExistingFlowLocked(completion.OutputFlowId);
                UpdateDurationLocked(flow, completion.SegmentId, duration);
            }
        }
        else
        {
            lock (_gate)
            {
                if (_flows.TryGetValue(completion.OutputFlowId, out var flow) &&
                    TryGetEntryLocked(flow, completion.SegmentId, out var entry) &&
                    entry.AccumulatedDuration > TimeSpan.Zero)
                {
                    entry.Duration = entry.AccumulatedDuration;
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(
        OutputFlowId outputFlowId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        OutputPlaybackEvent[] events;
        lock (_gate)
        {
            if (!_flows.TryGetValue(outputFlowId, out var flow))
            {
                yield break;
            }

            events = flow.Events.ToArray();
            flow.Events.Clear();
        }

        foreach (var playbackEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return playbackEvent;
            await Task.Yield();
        }
    }

    public ValueTask<OutputPlaybackBoundary> InterruptAsync(
        OutputFlowId outputFlowId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var flow = GetExistingFlowLocked(outputFlowId);
            var boundary = flow.Active is { } active
                ? CreateBoundary(active, flow.ResponseId)
                : CreateQueuedUnplayedBoundary(outputFlowId, flow.ResponseId);

            if (flow.Active is { } activeEntry)
            {
                flow.Events.Enqueue(new OutputPlaybackInterruptedEvent
                {
                    OutputFlowId = outputFlowId,
                    ResponseId = flow.ResponseId,
                    SegmentId = activeEntry.Request.SegmentId,
                    SegmentIndex = activeEntry.Request.SegmentIndex,
                    Boundary = boundary,
                    ObservedAt = _observedAt
                });
            }

            flow.Active = null;
            flow.Entries.RemoveAll(entry =>
                entry.Request.Interruptibility == OutputInterruptibility.Interruptible);

            return ValueTask.FromResult(boundary);
        }
    }

    public ValueTask FlushAsync(
        OutputFlowId outputFlowId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_flows.TryGetValue(outputFlowId, out var flow))
            {
                if (flow.Active is { } active)
                {
                    if (active.PlayedDuration > TimeSpan.Zero)
                    {
                        flow.Events.Enqueue(new OutputPlaybackInterruptedEvent
                        {
                            OutputFlowId = outputFlowId,
                            ResponseId = flow.ResponseId,
                            SegmentId = active.Request.SegmentId,
                            SegmentIndex = active.Request.SegmentIndex,
                            Boundary = CreateBoundary(active, flow.ResponseId),
                            ObservedAt = _observedAt
                        });
                    }
                    else
                    {
                        flow.Events.Enqueue(CreateClearedEvent(active.Request));
                    }
                }

                foreach (var entry in flow.Entries)
                {
                    flow.Events.Enqueue(CreateClearedEvent(entry.Request));
                }

                flow.Active = null;
                flow.Entries.Clear();
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask FailAsync(
        OutputFlowId outputFlowId,
        OutputSegmentId segmentId,
        AudioErrorInfo error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var flow = GetExistingFlowLocked(outputFlowId);
            PlaybackEntry? failed = null;
            if (flow.Active?.Request.SegmentId == segmentId)
            {
                failed = flow.Active;
                flow.Active = null;
            }
            else
            {
                var index = flow.Entries.FindIndex(entry => entry.Request.SegmentId == segmentId);
                if (index >= 0)
                {
                    failed = flow.Entries[index];
                    flow.Entries.RemoveAt(index);
                }
            }

            if (failed is null)
            {
                throw new InvalidOperationException("The requested output segment is not queued or active.");
            }

            flow.Events.Enqueue(new OutputPlaybackFailedEvent
            {
                OutputFlowId = outputFlowId,
                ResponseId = flow.ResponseId,
                SegmentId = failed.Request.SegmentId,
                SegmentIndex = failed.Request.SegmentIndex,
                Error = error,
                ObservedAt = _observedAt
            });
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask AdvanceAsync(
        OutputFlowId outputFlowId,
        TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed), "Elapsed time must be non-negative.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var flow = GetExistingFlowLocked(outputFlowId);
            _observedAt += elapsed;
            var remaining = elapsed;

            while (remaining >= TimeSpan.Zero)
            {
                var active = flow.Active ?? StartNextLocked(flow);
                if (active is null)
                {
                    break;
                }

                if (remaining == TimeSpan.Zero)
                {
                    EmitProgressLocked(flow, active);
                    break;
                }

                var segmentRemaining = active.Duration - active.PlayedDuration;
                var consumed = remaining <= segmentRemaining ? remaining : segmentRemaining;
                active.PlayedDuration += consumed;
                remaining -= consumed;

                if (active.PlayedDuration >= active.Duration)
                {
                    active.PlayedDuration = active.Duration;
                    flow.Events.Enqueue(new OutputPlaybackCompletedEvent
                    {
                        OutputFlowId = active.Request.OutputFlowId,
                        ResponseId = active.Request.ResponseId,
                        SegmentId = active.Request.SegmentId,
                        SegmentIndex = active.Request.SegmentIndex,
                        Cursor = CreateCursor(active),
                        ObservedAt = _observedAt
                    });
                    flow.Active = null;
                    continue;
                }

                EmitProgressLocked(flow, active);
                break;
            }
        }

        return ValueTask.CompletedTask;
    }

    private PlaybackEntry? StartNextLocked(FlowState flow)
    {
        if (flow.Entries.Count == 0)
        {
            return null;
        }

        var next = flow.Entries[0];
        flow.Entries.RemoveAt(0);
        flow.Active = next;
        flow.Events.Enqueue(new OutputPlaybackStartedEvent
        {
            OutputFlowId = next.Request.OutputFlowId,
            ResponseId = next.Request.ResponseId,
            SegmentId = next.Request.SegmentId,
            SegmentIndex = next.Request.SegmentIndex,
            ObservedAt = _observedAt
        });
        return next;
    }

    private void EmitProgressLocked(FlowState flow, PlaybackEntry active)
    {
        if (active.PlayedDuration <= TimeSpan.Zero || active.PlayedDuration >= active.Duration)
        {
            return;
        }

        flow.Events.Enqueue(new OutputPlaybackProgressEvent
        {
            OutputFlowId = active.Request.OutputFlowId,
            ResponseId = active.Request.ResponseId,
            SegmentId = active.Request.SegmentId,
            SegmentIndex = active.Request.SegmentIndex,
            Cursor = CreateCursor(active),
            ObservedAt = _observedAt
        });
    }

    private FlowState GetOrCreateFlowLocked(OutputFlowId outputFlowId, ResponseId responseId)
    {
        if (!_flows.TryGetValue(outputFlowId, out var flow))
        {
            flow = new FlowState(responseId);
            _flows.Add(outputFlowId, flow);
        }

        if (flow.ResponseId != responseId)
        {
            throw new InvalidOperationException("A manual output sink flow can only track one provider response.");
        }

        return flow;
    }

    private FlowState GetExistingFlowLocked(OutputFlowId outputFlowId)
    {
        if (!_flows.TryGetValue(outputFlowId, out var flow))
        {
            throw new InvalidOperationException("The output flow has no queued playback in this sink.");
        }

        return flow;
    }

    private TimeSpan ResolveDuration(OutputPlaybackRequest request)
    {
        var duration = request.EstimatedDuration ?? _defaultDuration;
        return duration > TimeSpan.Zero ? duration : _defaultDuration;
    }

    private AudioErrorInfo? ValidateStart(OutputAudioStream stream)
    {
        if (stream.PayloadKind == OutputAudioPayloadKind.DecodedPcmFrame)
        {
            return null;
        }

        if (stream.PayloadKind == OutputAudioPayloadKind.EncodedBytes && _acceptEncodedAudio)
        {
            return null;
        }

        return UnsupportedPayloadError(stream.PayloadKind, stream.MediaType);
    }

    private void WriteDecodedFrameLocked(
        FlowState flow,
        OutputAudioChunk chunk,
        HPD.Audio.Primitives.AudioFrame frame)
    {
        if (!TryGetEntryLocked(flow, chunk.SegmentId, out var entry))
        {
            RejectSegmentLocked(
                flow,
                chunk.OutputFlowId,
                chunk.ResponseId,
                chunk.SegmentId,
                chunk.SegmentIndex,
                new AudioErrorInfo
                {
                    Code = "ManualSinkStreamNotQueued",
                    Message = "The manual output sink received a decoded audio chunk for a stream that was not queued.",
                    Category = "Playback"
                });
            return;
        }

        if (!flow.Pipes.TryGetValue(chunk.SegmentId, out var pipe))
        {
            pipe = new BoundedAudioFramePipe(frame.Format, _pcmPipeCapacity);
            flow.Pipes.Add(chunk.SegmentId, pipe);
        }

        if (!pipe.TryWrite(frame))
        {
            RejectSegmentLocked(
                flow,
                chunk.OutputFlowId,
                chunk.ResponseId,
                chunk.SegmentId,
                chunk.SegmentIndex,
                new AudioErrorInfo
                {
                    Code = "ManualSinkPcmPipeRejectedFrame",
                    Message = "The manual output sink PCM frame pipe rejected a decoded audio frame.",
                    Category = "Playback"
                });
            return;
        }

        entry.AccumulatedDuration += frame.Duration;
    }

    private bool TryGetEntryLocked(
        FlowState flow,
        OutputSegmentId segmentId,
        out PlaybackEntry entry)
    {
        if (flow.Active?.Request.SegmentId == segmentId)
        {
            entry = flow.Active;
            return true;
        }

        foreach (var candidate in flow.Entries)
        {
            if (candidate.Request.SegmentId == segmentId)
            {
                entry = candidate;
                return true;
            }
        }

        entry = null!;
        return false;
    }

    private void UpdateDurationLocked(
        FlowState flow,
        OutputSegmentId segmentId,
        TimeSpan duration)
    {
        if (TryGetEntryLocked(flow, segmentId, out var entry))
        {
            entry.Duration = duration;
        }
    }

    private void RejectSegmentLocked(
        FlowState flow,
        OutputFlowId outputFlowId,
        ResponseId responseId,
        OutputSegmentId segmentId,
        int segmentIndex,
        AudioErrorInfo error)
    {
        if (!flow.RejectedSegments.Add(segmentId))
        {
            return;
        }

        if (flow.Active?.Request.SegmentId == segmentId)
        {
            flow.Active = null;
        }

        flow.Entries.RemoveAll(entry => entry.Request.SegmentId == segmentId);
        flow.Pipes.Remove(segmentId);
        flow.Events.Enqueue(new OutputPlaybackFailedEvent
        {
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = segmentIndex,
            Error = error,
            ObservedAt = _observedAt
        });
    }

    private static AudioErrorInfo UnsupportedPayloadError(
        OutputAudioPayloadKind payloadKind,
        string mediaType)
    {
        return new AudioErrorInfo
        {
            Code = "ManualSinkUnsupportedOutputAudioPayload",
            Message = $"The manual output sink requires decoded PCM frames; payload kind '{payloadKind}' with media type '{mediaType}' is not playable by this sink.",
            Category = "Playback"
        };
    }

    private OutputPlaybackBoundary CreateBoundary(PlaybackEntry active, ResponseId responseId)
    {
        var cursor = CreateCursor(active);
        return new OutputPlaybackBoundary
        {
            OutputFlowId = active.Request.OutputFlowId,
            ResponseId = responseId,
            SegmentId = active.Request.SegmentId,
            SegmentIndex = active.Request.SegmentIndex,
            PlayedDuration = cursor.PlayedDuration,
            PlayedTextLength = cursor.PlayedTextLength,
            Precision = cursor.Precision,
            ObservedAt = _observedAt
        };
    }

    private static OutputPlaybackBoundary CreateQueuedUnplayedBoundary(
        OutputFlowId outputFlowId,
        ResponseId responseId,
        OutputSegmentId? segmentId = null,
        int segmentIndex = 0)
    {
        return new OutputPlaybackBoundary
        {
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = segmentIndex,
            PlayedTextLength = 0,
            Precision = OutputAlignmentPrecision.LocalOnly
        };
    }

    private OutputPlaybackClearedEvent CreateClearedEvent(OutputPlaybackRequest request)
    {
        return new OutputPlaybackClearedEvent
        {
            OutputFlowId = request.OutputFlowId,
            ResponseId = request.ResponseId,
            SegmentId = request.SegmentId,
            SegmentIndex = request.SegmentIndex,
            Boundary = CreateQueuedUnplayedBoundary(
                request.OutputFlowId,
                request.ResponseId,
                request.SegmentId,
                request.SegmentIndex),
            ObservedAt = _observedAt
        };
    }

    private static OutputPlaybackCursor CreateCursor(PlaybackEntry active)
    {
        var textLength = Math.Max(0, active.Request.SourceTextLength);
        var playedInSegment = active.Duration <= TimeSpan.Zero
            ? textLength
            : (int)Math.Round(textLength * active.PlayedDuration.TotalMilliseconds / active.Duration.TotalMilliseconds);
        playedInSegment = Math.Clamp(playedInSegment, 0, textLength);

        return new OutputPlaybackCursor
        {
            OutputFlowId = active.Request.OutputFlowId,
            ResponseId = active.Request.ResponseId,
            SegmentId = active.Request.SegmentId,
            SegmentIndex = active.Request.SegmentIndex,
            PlayedDuration = active.PlayedDuration,
            PlayedTextLength = active.Request.SourceTextStart + playedInSegment,
            Precision = active.Request.Alignment?.Precision ?? OutputAlignmentPrecision.LocalOnly
        };
    }

    private sealed class FlowState(ResponseId responseId)
    {
        public ResponseId ResponseId { get; } = responseId;

        public List<PlaybackEntry> Entries { get; } = [];

        public Queue<OutputPlaybackEvent> Events { get; } = [];

        public Dictionary<OutputSegmentId, BoundedAudioFramePipe> Pipes { get; } = [];

        public HashSet<OutputSegmentId> RejectedSegments { get; } = [];

        public PlaybackEntry? Active { get; set; }
    }

    private sealed class PlaybackEntry(OutputPlaybackRequest request, TimeSpan duration)
    {
        public OutputPlaybackRequest Request { get; } = request;

        public TimeSpan Duration { get; set; } = duration;

        public TimeSpan AccumulatedDuration { get; set; }

        public TimeSpan PlayedDuration { get; set; }
    }
}
