using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Media;

namespace HPD.Agent.Audio.Runtime.Output;

public sealed class InMemoryOutputFlow : IOutputFlow
{
    private readonly List<OutputSegment> _segments = [];
    private readonly List<OutputAudioStream> _audioStreams = [];
    private readonly List<OutputAudioChunkMetadata> _audioChunks = [];
    private readonly List<OutputAudioArtifact> _audioArtifacts = [];
    private readonly object _gate = new();
    private OutputFlowState _state = OutputFlowState.Created;
    private ResponseId? _responseId;
    private OutputPlaybackBoundary? _playbackBoundary;
    private string? _completionReason;

    public InMemoryOutputFlow(OutputFlowId id)
    {
        Id = id;
    }

    public OutputFlowId Id { get; }

    public OutputFlowSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return SnapshotLocked();
            }
        }
    }

    public ValueTask AppendTextAsync(
        ResponseId responseId,
        string text,
        bool isFinal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ThrowIfTerminalLocked("Cannot append text after a terminal disposition.");
            EnsureResponseLocked(responseId);

            _segments.Add(new OutputTextSegment
            {
                Id = NextSyntheticSegmentIdLocked("text"),
                ResponseId = responseId,
                Text = text
            });
            _state = isFinal ? OutputFlowState.TextReady : OutputFlowState.GeneratingText;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StartAudioStreamAsync(
        OutputAudioStream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (stream.OutputFlowId != Id)
            {
                throw new InvalidOperationException("Audio stream targets a different output flow.");
            }

            ThrowIfTerminalLocked("Cannot start audio stream after a terminal disposition.");
            EnsureResponseLocked(stream.ResponseId);

            _audioStreams.Add(stream);
            _segments.Add(new OutputAudioSegment
            {
                Id = stream.SegmentId,
                ResponseId = stream.ResponseId,
                Payload = new MediaPayloadRef.MetadataOnly(null, "Output audio stream metadata without retained payload."),
                Format = new MediaFormatDescriptor
                {
                    MediaType = stream.MediaType
                },
                SegmentIndex = stream.SegmentIndex,
                IsFinalSegment = stream.IsFinalSegment,
                SourceTextStart = stream.SourceTextStart,
                SourceTextLength = stream.SourceTextLength,
                Alignment = stream.Alignment
            });
            _state = OutputFlowState.AudioStreaming;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask AppendAudioChunkAsync(
        OutputAudioChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (chunk.OutputFlowId != Id)
            {
                throw new InvalidOperationException("Audio chunk targets a different output flow.");
            }

            ThrowIfTerminalLocked("Cannot append audio chunk after a terminal disposition.");
            EnsureResponseLocked(chunk.ResponseId);

            if (_audioStreams.All(stream => stream.SegmentId != chunk.SegmentId))
            {
                throw new InvalidOperationException("Cannot append audio chunk before its stream is started.");
            }

            _audioChunks.Add(new OutputAudioChunkMetadata
            {
                OutputFlowId = chunk.OutputFlowId,
                ResponseId = chunk.ResponseId,
                SegmentId = chunk.SegmentId,
                SegmentIndex = chunk.SegmentIndex,
                Sequence = chunk.Sequence,
                PayloadKind = chunk.Payload.Kind,
                MediaType = chunk.MediaType,
                SizeBytes = chunk.SizeBytes,
                Duration = chunk.Duration,
                ObservedAt = chunk.ObservedAt,
                IsFinalChunk = chunk.IsFinalChunk
            });
            _state = OutputFlowState.AudioStreaming;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CompleteAudioStreamAsync(
        OutputAudioStreamCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (completion.OutputFlowId != Id)
            {
                throw new InvalidOperationException("Audio stream completion targets a different output flow.");
            }

            ThrowIfTerminalLocked("Cannot complete audio stream after a terminal disposition.");
            EnsureResponseLocked(completion.ResponseId);

            _state = completion.Disposition switch
            {
                OutputAudioStreamDisposition.Completed => OutputFlowState.AudioStreamCompleted,
                OutputAudioStreamDisposition.Canceled => OutputFlowState.Canceled,
                OutputAudioStreamDisposition.Interrupted => OutputFlowState.Interrupted,
                _ => OutputFlowState.Failed
            };
            _completionReason = completion.Error?.Message;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask AttachAudioArtifactAsync(
        OutputAudioArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (artifact.OutputFlowId != Id)
            {
                throw new InvalidOperationException("Audio artifact targets a different output flow.");
            }

            ThrowIfTerminalLocked("Cannot attach audio artifact after a terminal disposition.");
            EnsureResponseLocked(artifact.ResponseId);

            _audioArtifacts.Add(artifact);
            _state = OutputFlowState.ArtifactCaptured;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkQueuedAsync(
        OutputPlaybackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (request.OutputFlowId != Id)
            {
                throw new InvalidOperationException("Playback request targets a different output flow.");
            }

            ThrowIfTerminalLocked("Cannot queue playback after a terminal disposition.");
            EnsureResponseLocked(request.ResponseId);

            _state = OutputFlowState.Queued;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkPlaybackStartedAsync(
        OutputPlaybackEvent playbackStarted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playbackStarted);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ValidatePlaybackEventLocked(playbackStarted);
            ThrowIfTerminalLocked("Cannot start playback after a terminal disposition.");
            _state = OutputFlowState.Playing;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdatePlaybackProgressAsync(
        OutputPlaybackCursor cursor,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ValidateCursorLocked(cursor);
            ThrowIfTerminalLocked("Cannot update playback progress after a terminal disposition.");
            _playbackBoundary = ToBoundary(cursor);
            _state = OutputFlowState.Playing;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<OutputCommitRecord> CompletePlayedAsync(
        OutputPlaybackCursor cursor,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_state is OutputFlowState.PlayedComplete)
            {
                return ValueTask.FromResult(CreateCommitLocked(OutputCommitDisposition.PlayedComplete));
            }

            ValidateCursorLocked(cursor);
            ThrowIfTerminalLocked("Cannot complete played output after a terminal disposition.");

            _playbackBoundary = ToBoundary(cursor);
            _state = OutputFlowState.PlayedComplete;
            return ValueTask.FromResult(CreateCommitLocked(OutputCommitDisposition.PlayedComplete));
        }
    }

    public ValueTask<OutputCommitRecord> CommitInterruptedAsync(
        OutputPlaybackBoundary boundary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (boundary.OutputFlowId != Id)
            {
                throw new InvalidOperationException("Playback boundary targets a different output flow.");
            }

            EnsureResponseLocked(boundary.ResponseId);

            ThrowIfTerminalLocked("Cannot interrupt an output flow after a terminal disposition.");

            var text = TextLocked();
            if (boundary.PlayedTextLength < 0 || boundary.PlayedTextLength > text.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(boundary),
                    "Played text length must fall within the generated output text.");
            }

            _playbackBoundary = boundary;
            _state = OutputFlowState.Interrupted;

            return ValueTask.FromResult(new OutputCommitRecord
            {
                OutputFlowId = Id,
                ResponseId = boundary.ResponseId,
                Disposition = OutputCommitDisposition.Interrupted,
                Text = text[..boundary.PlayedTextLength],
                PlaybackBoundary = boundary
            });
        }
    }

    public ValueTask<OutputCommitRecord> FailPlaybackAsync(
        OutputPlaybackFailure failure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (failure.OutputFlowId != Id)
            {
                throw new InvalidOperationException("Playback failure targets a different output flow.");
            }

            EnsureResponseLocked(failure.ResponseId);

            if (_state is OutputFlowState.PlaybackFailed)
            {
                return ValueTask.FromResult(CreateCommitLocked(OutputCommitDisposition.PlaybackFailed));
            }

            ThrowIfTerminalLocked("Cannot fail playback after a terminal disposition.");

            _state = OutputFlowState.PlaybackFailed;
            _completionReason = failure.Error.Message;
            return ValueTask.FromResult(CreateCommitLocked(OutputCommitDisposition.PlaybackFailed));
        }
    }

    public ValueTask<OutputCommitRecord> CommitQueuedUnplayedAsync(
        OutputPlaybackBoundary boundary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (boundary.OutputFlowId != Id)
            {
                throw new InvalidOperationException("Playback boundary targets a different output flow.");
            }

            EnsureResponseLocked(boundary.ResponseId);

            if (_state is OutputFlowState.QueuedUnplayed)
            {
                return ValueTask.FromResult(CreateCommitLocked(OutputCommitDisposition.QueuedUnplayed));
            }

            ThrowIfTerminalLocked("Cannot commit queued-unplayed output after a terminal disposition.");

            if (boundary.PlayedTextLength != 0 || boundary.PlayedDuration != TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(boundary),
                    "Queued-unplayed output must have a zero playback boundary.");
            }

            _playbackBoundary = boundary;
            _state = OutputFlowState.QueuedUnplayed;
            _completionReason = "Output was queued but cleared before playback.";
            return ValueTask.FromResult(CreateCommitLocked(OutputCommitDisposition.QueuedUnplayed));
        }
    }

    public ValueTask<OutputCommitRecord> CompleteSynthesizedNotPlayedAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_state is OutputFlowState.SynthesizedNotPlayed)
            {
                return ValueTask.FromResult(CreateCommitLocked(OutputCommitDisposition.SynthesizedNotPlayed));
            }

            ThrowIfTerminalLocked("Cannot complete synthesized output after a terminal disposition.");

            if (_audioStreams.Count == 0 && _audioArtifacts.Count == 0)
            {
                throw new InvalidOperationException("Cannot complete synthesized output before audio is available.");
            }

            _state = OutputFlowState.SynthesizedNotPlayed;
            _completionReason = "Synthesized audio available; no playback sink observed.";
            return ValueTask.FromResult(CreateCommitLocked(OutputCommitDisposition.SynthesizedNotPlayed));
        }
    }

    public ValueTask<OutputCommitRecord> CompleteTextOnlyAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_state is OutputFlowState.TextOnlyCompleted)
            {
                return ValueTask.FromResult(CreateCommitLocked(OutputCommitDisposition.TextOnly));
            }

            ThrowIfTerminalLocked("Cannot complete text-only output after a terminal disposition.");

            _state = OutputFlowState.TextOnlyCompleted;
            _completionReason = reason;
            return ValueTask.FromResult(CreateCommitLocked(OutputCommitDisposition.TextOnly));
        }
    }

    private OutputFlowSnapshot SnapshotLocked()
    {
        return new OutputFlowSnapshot
        {
            Id = Id,
            State = _state,
            ResponseId = _responseId,
            SegmentIds = _segments.Select(segment => segment.Id).ToArray(),
            Text = TextLocked(),
            AudioStreams = _audioStreams.ToArray(),
            AudioChunks = _audioChunks.ToArray(),
            AudioArtifacts = _audioArtifacts.ToArray(),
            PlaybackBoundary = _playbackBoundary
        };
    }

    private OutputCommitRecord CreateCommitLocked(OutputCommitDisposition disposition)
    {
        return new OutputCommitRecord
        {
            OutputFlowId = Id,
            ResponseId = _responseId ?? new ResponseId("response-unknown"),
            Disposition = disposition,
            Text = TextLocked(),
            AudioStreams = _audioStreams.ToArray(),
            AudioArtifacts = _audioArtifacts.ToArray(),
            PlaybackBoundary = _playbackBoundary,
            Reason = _completionReason
        };
    }

    private void ValidatePlaybackEventLocked(OutputPlaybackEvent playbackEvent)
    {
        if (playbackEvent.OutputFlowId != Id)
        {
            throw new InvalidOperationException("Playback event targets a different output flow.");
        }

        EnsureResponseLocked(playbackEvent.ResponseId);
    }

    private void ValidateCursorLocked(OutputPlaybackCursor cursor)
    {
        if (cursor.OutputFlowId != Id)
        {
            throw new InvalidOperationException("Playback cursor targets a different output flow.");
        }

        EnsureResponseLocked(cursor.ResponseId);

        var text = TextLocked();
        if (cursor.PlayedTextLength < 0 || cursor.PlayedTextLength > text.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cursor),
                "Played text length must fall within the generated output text.");
        }
    }

    private static OutputPlaybackBoundary ToBoundary(OutputPlaybackCursor cursor)
    {
        return new OutputPlaybackBoundary
        {
            OutputFlowId = cursor.OutputFlowId,
            ResponseId = cursor.ResponseId,
            SegmentId = cursor.SegmentId,
            SegmentIndex = cursor.SegmentIndex,
            PlayedDuration = cursor.PlayedDuration,
            PlayedTextLength = cursor.PlayedTextLength,
            Precision = cursor.Precision,
            ObservedAt = cursor.ObservedAt
        };
    }

    private string TextLocked()
    {
        return string.Concat(_segments.OfType<OutputTextSegment>().Select(segment => segment.Text));
    }

    private void EnsureResponseLocked(ResponseId responseId)
    {
        _responseId ??= responseId;
        if (_responseId.Value != responseId)
        {
            throw new InvalidOperationException("An output flow can only track one provider response.");
        }
    }

    private void ThrowIfTerminalLocked(string message)
    {
        if (IsTerminal(_state))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static bool IsTerminal(OutputFlowState state)
    {
        return state is OutputFlowState.Interrupted
            or OutputFlowState.Canceled
            or OutputFlowState.Truncated
            or OutputFlowState.Failed
            or OutputFlowState.TextOnlyCompleted
            or OutputFlowState.SynthesizedNotPlayed
            or OutputFlowState.PlayedPartial
            or OutputFlowState.PlayedComplete
            or OutputFlowState.QueuedUnplayed
            or OutputFlowState.PlaybackFailed;
    }

    private OutputSegmentId NextSyntheticSegmentIdLocked(string kind)
    {
        return new OutputSegmentId($"{Id.Value}:{kind}-{_segments.Count + 1:D4}");
    }

}
