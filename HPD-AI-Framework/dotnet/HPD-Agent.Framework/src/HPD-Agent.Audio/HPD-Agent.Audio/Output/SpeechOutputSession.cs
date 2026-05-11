// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace HPD.Agent.Audio.Output;

/// <summary>
/// Basic speech output session that records queued output and emits normalized output events.
/// </summary>
public sealed class SpeechOutputSession : ISpeechOutputSession
{
    private readonly Channel<SpeechOutputEvent> _events = Channel.CreateUnbounded<SpeechOutputEvent>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private readonly string? _runtimeId;
    private readonly string? _sessionId;
    private readonly string? _branchId;
    private readonly string? _synthesisId;
    private readonly string? _provider;
    private readonly string? _model;
    private readonly string? _voice;
    private readonly object _lock = new();
    private bool _started;
    private bool _playbackStarted;
    private bool _playbackFinished;
    private bool _completed;
    private bool _disposed;
    private DateTimeOffset? _pausedAt;
    private SpeechOutputState _state = new();

    /// <summary>
    /// Creates a speech output session.
    /// </summary>
    public SpeechOutputSession(
        string speechId,
        string streamId,
        string? runtimeId = null,
        string? sessionId = null,
        string? branchId = null,
        string? synthesisId = null,
        string? provider = null,
        string? model = null,
        string? voice = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speechId);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        SpeechId = speechId;
        StreamId = streamId;
        _runtimeId = runtimeId;
        _sessionId = sessionId;
        _branchId = branchId;
        _synthesisId = synthesisId;
        _provider = provider;
        _model = model;
        _voice = voice;
    }

    /// <inheritdoc />
    public string SpeechId { get; }

    /// <inheritdoc />
    public string StreamId { get; }

    /// <inheritdoc />
    public SpeechOutputState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SpeechOutputEvent> Events => ReadEventsAsync();

    /// <inheritdoc />
    public async ValueTask PushTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(text);
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        await WriteAsync(new SpeechOutputTextQueuedEvent
        {
            Context = CreateContext(),
            Text = text
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask PushAudioAsync(AudioChunkFrame frame, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        SpeechOutputState state;
        lock (_lock)
        {
            _state = _state with
            {
                GeneratedDuration = _state.GeneratedDuration + frame.Duration,
                QueuedDuration = _state.QueuedDuration + frame.Duration,
                QueuedChunks = _state.QueuedChunks + 1,
                EmittedChunks = _state.EmittedChunks + 1,
                HeldDuration = _state.IsPaused
                    ? _state.HeldDuration + frame.Duration
                    : _state.HeldDuration,
                HeldChunks = _state.IsPaused
                    ? _state.HeldChunks + 1
                    : _state.HeldChunks
            };
            state = _state;
        }

        await WriteAsync(new SpeechOutputAudioQueuedEvent
        {
            Context = CreateContext(frame.SequenceNumber, frame.TimestampNs),
            Frame = frame,
            State = state
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask MarkPlaybackStartedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        SpeechOutputState state;
        lock (_lock)
        {
            if (_playbackStarted)
                return;

            _playbackStarted = true;
            state = _state;
        }

        await WriteAsync(new SpeechOutputPlaybackStartedEvent
        {
            Context = CreateContext(),
            State = state
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask MarkPlaybackProgressAsync(
        TimeSpan playedDuration,
        TimeSpan playbackPosition,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (playedDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(playedDuration));
        if (playbackPosition < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(playbackPosition));

        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        if (!_playbackStarted)
            await MarkPlaybackStartedAsync(cancellationToken).ConfigureAwait(false);

        SpeechOutputState state;
        lock (_lock)
        {
            if (_completed || _playbackFinished)
                return;

            var playedChunks = playedDuration > _state.PlayedDuration
                ? _state.PlayedChunks + 1
                : _state.PlayedChunks;

            _state = _state with
            {
                PlayedDuration = playedDuration,
                PlaybackPosition = playbackPosition,
                PlayedChunks = playedChunks
            };
            state = _state;
        }

        await WriteAsync(new SpeechOutputPlaybackProgressEvent
        {
            Context = CreateContext(),
            State = state
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask MarkPlaybackFinishedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        if (!_playbackStarted)
            await MarkPlaybackStartedAsync(cancellationToken).ConfigureAwait(false);

        SpeechOutputState state;
        lock (_lock)
        {
            if (_playbackFinished)
                return;

            _playbackFinished = true;
            _state = _state with
            {
                PlayedDuration = Max(_state.PlayedDuration, _state.QueuedDuration),
                PlaybackPosition = Max(_state.PlaybackPosition, _state.QueuedDuration),
                PlayedChunks = Math.Max(_state.PlayedChunks, _state.QueuedChunks)
            };
            state = _state;
        }

        await WriteAsync(new SpeechOutputPlaybackFinishedEvent
        {
            Context = CreateContext(),
            State = state
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        SpeechOutputState state;
        lock (_lock)
        {
            if (_completed)
                return;

            _completed = true;
            if (_state.PlayedDuration >= _state.GeneratedDuration)
                _playbackFinished = true;
            state = _state;
        }

        await WriteAsync(new SpeechOutputCompletedEvent
        {
            Context = CreateContext(),
            State = state
        }, cancellationToken).ConfigureAwait(false);
        _events.Writer.TryComplete();
    }

    /// <inheritdoc />
    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        SpeechOutputState state;
        lock (_lock)
        {
            _pausedAt ??= DateTimeOffset.UtcNow;
            var heldDuration = _state.GeneratedDuration > _state.PlayedDuration
                ? _state.GeneratedDuration - _state.PlayedDuration
                : _state.HeldDuration;

            _state = _state with
            {
                IsPaused = true,
                HeldDuration = heldDuration,
                HeldChunks = Math.Max(_state.HeldChunks, _state.QueuedChunks - _state.PlayedChunks)
            };
            state = _state;
        }

        await WriteAsync(new SpeechOutputPausedEvent
        {
            Context = CreateContext(),
            Reason = "pause_requested",
            State = state
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        TimeSpan? pauseDuration = null;
        SpeechOutputState state;
        lock (_lock)
        {
            if (_pausedAt is { } pausedAt)
            {
                pauseDuration = DateTimeOffset.UtcNow - pausedAt;
                _pausedAt = null;
            }

            _state = _state with
            {
                IsPaused = false,
                HeldDuration = TimeSpan.Zero,
                HeldChunks = 0
            };
            state = _state;
        }

        await WriteAsync(new SpeechOutputResumedEvent
        {
            Context = CreateContext(),
            PauseDuration = pauseDuration,
            State = state
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask InterruptAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        SpeechOutputState state;
        lock (_lock)
        {
            if (_completed)
                return;

            _completed = true;
            var discardedDuration = _state.GeneratedDuration > _state.PlayedDuration
                ? _state.GeneratedDuration - _state.PlayedDuration
                : TimeSpan.Zero;

            _state = _state with
            {
                Interrupted = true,
                IsPaused = false,
                DiscardedDuration = discardedDuration
            };
            state = _state;
        }

        await WriteAsync(new SpeechOutputInterruptedEvent
        {
            Context = CreateContext(),
            Reason = "interrupt_requested",
            State = state
        }, cancellationToken).ConfigureAwait(false);
        _events.Writer.TryComplete();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private async ValueTask EnsureStartedAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_started)
                return;

            _started = true;
        }

        await WriteAsync(new SpeechOutputStartedEvent
        {
            Context = CreateContext()
        }, cancellationToken).ConfigureAwait(false);
    }

    private SpeechOutputContext CreateContext(long? sequenceNumber = null, long? timestampNs = null) =>
        new(
            RuntimeId: _runtimeId,
            SessionId: _sessionId,
            BranchId: _branchId,
            SpeechId: SpeechId,
            StreamId: StreamId,
            SynthesisId: _synthesisId,
            Provider: _provider,
            Model: _model,
            Voice: _voice,
            SequenceNumber: sequenceNumber,
            TimestampNs: timestampNs,
            ObservedAt: DateTimeOffset.UtcNow);

    private ValueTask WriteAsync(SpeechOutputEvent evt, CancellationToken cancellationToken) =>
        _events.Writer.WriteAsync(evt, cancellationToken);

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;

    private async IAsyncEnumerable<SpeechOutputEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return evt;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
