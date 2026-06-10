#nullable enable

namespace HPD.Audio.Primitives;

/// <summary>
/// Provides a bounded in-memory audio source/sink pair backed by a preallocated ring buffer.
/// </summary>
public sealed class BoundedAudioFramePipe : IAudioSource, IAudioSink
{
    private readonly object gate = new();
    private readonly AudioFrame[] frames;
    private readonly Queue<PendingRead> pendingReaders = new();
    private readonly Queue<PendingWrite> pendingWriters = new();
    private readonly Queue<PendingFlush> pendingFlushes = new();
    private int head;
    private int count;
    private long acceptedFrameCount;
    private long drainedFrameCount;
    private bool completing;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BoundedAudioFramePipe"/> class.
    /// </summary>
    public BoundedAudioFramePipe(AudioFormat format, int capacity, bool canChangeFormat = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (!IsUsableFormat(format))
        {
            throw new ArgumentException("The bounded audio frame pipe format must declare PCM16 with a positive sample rate and channel count.", nameof(format));
        }

        Format = format;
        PreferredFormat = format;
        CanChangeFormat = canChangeFormat;
        frames = new AudioFrame[capacity];
    }

    /// <inheritdoc />
    public AudioFormat Format { get; }

    /// <inheritdoc />
    public bool CanChangeFormat { get; }

    /// <inheritdoc />
    public AudioSourceState State
    {
        get
        {
            lock (gate)
            {
                if (disposed)
                {
                    return AudioSourceState.Disposed;
                }

                return completing && count == 0 ? AudioSourceState.Completed : AudioSourceState.Open;
            }
        }
    }

    /// <inheritdoc />
    public AudioFormat? PreferredFormat { get; }

    /// <inheritdoc />
    AudioSinkState IAudioSink.State
    {
        get
        {
            lock (gate)
            {
                if (disposed)
                {
                    return AudioSinkState.Disposed;
                }

                return completing
                    ? count == 0 ? AudioSinkState.Completed : AudioSinkState.Completing
                    : AudioSinkState.Open;
            }
        }
    }

    /// <summary>
    /// Gets the number of frames currently buffered.
    /// </summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return count;
            }
        }
    }

    /// <summary>
    /// Gets the maximum number of frames retained by the pipe.
    /// </summary>
    public int Capacity => frames.Length;

    /// <inheritdoc />
    public bool TryWrite(in AudioFrame frame)
    {
        PendingRead? reader = null;
        PendingFlush[] flushes = [];
        bool handedOff = false;
        lock (gate)
        {
            if (disposed || completing || !IsCompatibleFrame(frame))
            {
                return false;
            }

            while (pendingReaders.Count > 0)
            {
                PendingRead candidate = pendingReaders.Dequeue();
                if (!candidate.Task.IsCompleted)
                {
                    reader = candidate;
                    break;
                }

                candidate.DisposeCancellationRegistration();
            }

            if (reader is null && (count == frames.Length || pendingWriters.Count > 0))
            {
                PruneCompletedPendingWriters();
            }

            if (reader is null && (count == frames.Length || pendingWriters.Count > 0))
            {
                return false;
            }

            if (reader is null)
            {
                EnqueueFrame(frame);
                return true;
            }

            if (reader.TrySetResult(new AudioReadResult { HasFrame = true, Frame = frame }))
            {
                acceptedFrameCount++;
                drainedFrameCount++;
                flushes = DrainReadyFlushes();
                handedOff = true;
            }
        }

        if (!handedOff)
        {
            return TryWrite(frame);
        }

        CompleteFlushes(flushes);
        return true;
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(AudioFrame frame, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryWrite(frame))
        {
            return ValueTask.CompletedTask;
        }

        PendingRead? reader = null;
        PendingWrite? writer = null;
        PendingFlush[] flushes = [];
        bool handedOff = false;
        lock (gate)
        {
            if (disposed)
            {
                throw new AudioSinkException(AudioStreamErrorKind.Disposed, "The bounded audio frame pipe is disposed.");
            }

            if (completing)
            {
                throw new AudioSinkException(AudioStreamErrorKind.AlreadyCompleted, "The bounded audio frame pipe is already completed.");
            }

            if (!IsCompatibleFrame(frame))
            {
                throw new AudioSinkException(AudioStreamErrorKind.FormatMismatch, "The audio frame format does not match the bounded audio frame pipe format.");
            }

            while (pendingReaders.Count > 0)
            {
                PendingRead candidate = pendingReaders.Dequeue();
                if (!candidate.Task.IsCompleted)
                {
                    reader = candidate;
                    break;
                }

                candidate.DisposeCancellationRegistration();
            }

            if (reader is null && pendingWriters.Count > 0)
            {
                PruneCompletedPendingWriters();
            }

            if (reader is null && count < frames.Length && pendingWriters.Count == 0)
            {
                EnqueueFrame(frame);
                return ValueTask.CompletedTask;
            }

            if (reader is null)
            {
                writer = new PendingWrite(frame);
                if (cancellationToken.CanBeCanceled)
                {
                    writer.CancellationRegistration = cancellationToken.Register(
                        static state => ((PendingWrite)state!).TryCancel(),
                        writer);
                }

                pendingWriters.Enqueue(writer);
                return new ValueTask(writer.Task);
            }

            if (reader.TrySetResult(new AudioReadResult { HasFrame = true, Frame = frame }))
            {
                acceptedFrameCount++;
                drainedFrameCount++;
                flushes = DrainReadyFlushes();
                handedOff = true;
            }
        }

        if (!handedOff)
        {
            return WriteAsync(frame, cancellationToken);
        }

        CompleteFlushes(flushes);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public bool TryRead(out AudioFrame frame)
    {
        PendingWrite? promotedWriter;
        PendingFlush[] flushes;
        lock (gate)
        {
            if (count == 0)
            {
                frame = default;
                return false;
            }

            frame = DequeueFrame();
            flushes = DrainReadyFlushes();
            promotedWriter = PromotePendingWriter();
        }

        CompleteFlushes(flushes);
        promotedWriter?.TryComplete();
        return true;
    }

    /// <inheritdoc />
    public ValueTask<AudioReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryRead(out AudioFrame frame))
        {
            return new ValueTask<AudioReadResult>(new AudioReadResult { HasFrame = true, Frame = frame });
        }

        lock (gate)
        {
            if (disposed)
            {
                throw new AudioSourceException(AudioStreamErrorKind.Disposed, "The bounded audio frame pipe is disposed.");
            }

            if (completing)
            {
                return new ValueTask<AudioReadResult>(new AudioReadResult { HasFrame = false });
            }

            var reader = new PendingRead();
            if (cancellationToken.CanBeCanceled)
            {
                reader.CancellationRegistration = cancellationToken.Register(
                    static state => ((PendingRead)state!).TryCancel(),
                    reader);
            }

            pendingReaders.Enqueue(reader);
            return new ValueTask<AudioReadResult>(reader.Task);
        }
    }

    /// <inheritdoc />
    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (disposed)
            {
                throw new AudioSinkException(AudioStreamErrorKind.Disposed, "The bounded audio frame pipe is disposed.");
            }

            if (drainedFrameCount >= acceptedFrameCount)
            {
                return ValueTask.CompletedTask;
            }

            var flush = new PendingFlush(acceptedFrameCount);
            if (cancellationToken.CanBeCanceled)
            {
                flush.CancellationRegistration = cancellationToken.Register(
                    static state => ((PendingFlush)state!).TryCancel(),
                    flush);
            }

            pendingFlushes.Enqueue(flush);
            return new ValueTask(flush.Task);
        }
    }

    /// <inheritdoc />
    public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PendingRead[] readers;
        PendingWrite[] writers;
        PendingFlush[] flushes;
        PendingFlush? completionFlush = null;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            completing = true;
            writers = DrainPendingWriters();
            flushes = DrainReadyFlushes();
            if (drainedFrameCount >= acceptedFrameCount)
            {
                readers = DrainPendingReaders();
            }
            else
            {
                readers = [];
                completionFlush = new PendingFlush(acceptedFrameCount);
                if (cancellationToken.CanBeCanceled)
                {
                    completionFlush.CancellationRegistration = cancellationToken.Register(
                        static state => ((PendingFlush)state!).TryCancel(),
                        completionFlush);
                }

                pendingFlushes.Enqueue(completionFlush);
            }
        }

        CompleteReaders(readers);
        CompleteWriters(writers, AudioStreamErrorKind.AlreadyCompleted, "The bounded audio frame pipe is already completed.");
        CompleteFlushes(flushes);
        return completionFlush is null ? ValueTask.CompletedTask : new ValueTask(completionFlush.Task);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        PendingRead[] readers;
        PendingWrite[] writers;
        PendingFlush[] flushes;
        lock (gate)
        {
            disposed = true;
            completing = true;
            Array.Clear(frames);
            head = 0;
            count = 0;
            readers = DrainPendingReaders();
            writers = DrainPendingWriters();
            flushes = DrainPendingFlushes();
        }

        FailReaders(readers, AudioStreamErrorKind.Disposed, "The bounded audio frame pipe is disposed.");
        CompleteWriters(writers, AudioStreamErrorKind.Disposed, "The bounded audio frame pipe is disposed.");
        FailFlushes(flushes, AudioStreamErrorKind.Disposed, "The bounded audio frame pipe is disposed.");
        return ValueTask.CompletedTask;
    }

    private void EnqueueFrame(in AudioFrame frame)
    {
        int tail = (head + count) % frames.Length;
        frames[tail] = frame;
        count++;
        acceptedFrameCount++;
    }

    private AudioFrame DequeueFrame()
    {
        AudioFrame frame = frames[head];
        frames[head] = default;
        head = (head + 1) % frames.Length;
        count--;
        drainedFrameCount++;
        return frame;
    }

    private PendingWrite? PromotePendingWriter()
    {
        while (pendingWriters.Count > 0 && count < frames.Length)
        {
            PendingWrite writer = pendingWriters.Dequeue();
            if (writer.Task.IsCompleted)
            {
                writer.DisposeCancellationRegistration();
                continue;
            }

            EnqueueFrame(writer.Frame);
            return writer;
        }

        return null;
    }

    private void PruneCompletedPendingWriters()
    {
        while (pendingWriters.Count > 0 && pendingWriters.Peek().Task.IsCompleted)
        {
            pendingWriters.Dequeue().DisposeCancellationRegistration();
        }
    }

    private PendingRead[] DrainPendingReaders()
    {
        if (pendingReaders.Count == 0)
        {
            return [];
        }

        var readers = pendingReaders.ToArray();
        pendingReaders.Clear();
        return readers;
    }

    private PendingWrite[] DrainPendingWriters()
    {
        if (pendingWriters.Count == 0)
        {
            return [];
        }

        var writers = pendingWriters.ToArray();
        pendingWriters.Clear();
        return writers;
    }

    private PendingFlush[] DrainReadyFlushes()
    {
        if (pendingFlushes.Count == 0)
        {
            return [];
        }

        List<PendingFlush>? ready = null;
        while (pendingFlushes.Count > 0)
        {
            PendingFlush flush = pendingFlushes.Peek();
            if (flush.Task.IsCompleted)
            {
                pendingFlushes.Dequeue().DisposeCancellationRegistration();
                continue;
            }

            if (drainedFrameCount < flush.TargetAcceptedFrameCount)
            {
                break;
            }

            ready ??= new List<PendingFlush>();
            ready.Add(pendingFlushes.Dequeue());
        }

        return ready?.ToArray() ?? [];
    }

    private PendingFlush[] DrainPendingFlushes()
    {
        if (pendingFlushes.Count == 0)
        {
            return [];
        }

        var flushes = pendingFlushes.ToArray();
        pendingFlushes.Clear();
        return flushes;
    }

    private static void CompleteReaders(ReadOnlySpan<PendingRead> readers)
    {
        var result = new AudioReadResult { HasFrame = false };
        foreach (PendingRead reader in readers)
        {
            reader.TrySetResult(result);
        }
    }

    private static void FailReaders(ReadOnlySpan<PendingRead> readers, AudioStreamErrorKind kind, string message)
    {
        foreach (PendingRead reader in readers)
        {
            reader.TrySetException(new AudioSourceException(kind, message));
        }
    }

    private static void CompleteWriters(ReadOnlySpan<PendingWrite> writers, AudioStreamErrorKind kind, string message)
    {
        foreach (PendingWrite writer in writers)
        {
            writer.TrySetException(new AudioSinkException(kind, message));
        }
    }

    private static void CompleteFlushes(ReadOnlySpan<PendingFlush> flushes)
    {
        foreach (PendingFlush flush in flushes)
        {
            flush.TryComplete();
        }
    }

    private static void FailFlushes(ReadOnlySpan<PendingFlush> flushes, AudioStreamErrorKind kind, string message)
    {
        foreach (PendingFlush flush in flushes)
        {
            flush.TrySetException(new AudioSinkException(kind, message));
        }
    }

    private static bool FormatsEqual(in AudioFormat left, in AudioFormat right)
    {
        return left.SampleRate == right.SampleRate
            && left.ChannelCount == right.ChannelCount
            && left.SampleFormat == right.SampleFormat;
    }

    private static bool IsUsableFormat(in AudioFormat format)
    {
        return format.SampleFormat == AudioSampleFormat.Pcm16 &&
            format.SampleRate > 0 &&
            format.ChannelCount > 0;
    }

    private bool IsCompatibleFrame(in AudioFrame frame)
    {
        return IsUsableFrame(frame) &&
            (CanChangeFormat || FormatsEqual(frame.Format, Format));
    }

    private static bool IsUsableFrame(in AudioFrame frame)
    {
        if (!IsUsableFormat(frame.Format) || frame.SamplesPerChannel <= 0)
        {
            return false;
        }

        long expectedBytes = (long)frame.SamplesPerChannel * frame.Format.ChannelCount * sizeof(short);
        return expectedBytes == frame.Data.Length;
    }

    private sealed class PendingWrite
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingWrite(AudioFrame frame)
        {
            Frame = frame;
        }

        public AudioFrame Frame { get; }

        public Task Task => completion.Task;

        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public void TryComplete()
        {
            DisposeCancellationRegistration();
            completion.TrySetResult();
        }

        public void TryCancel()
        {
            DisposeCancellationRegistration();
            completion.TrySetCanceled();
        }

        public void TrySetException(Exception exception)
        {
            DisposeCancellationRegistration();
            completion.TrySetException(exception);
        }

        public void DisposeCancellationRegistration()
        {
            CancellationRegistration.Dispose();
        }
    }

    private sealed class PendingRead
    {
        private readonly TaskCompletionSource<AudioReadResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AudioReadResult> Task => completion.Task;

        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public bool TrySetResult(AudioReadResult result)
        {
            DisposeCancellationRegistration();
            return completion.TrySetResult(result);
        }

        public void TryCancel()
        {
            DisposeCancellationRegistration();
            completion.TrySetCanceled();
        }

        public void TrySetException(Exception exception)
        {
            DisposeCancellationRegistration();
            completion.TrySetException(exception);
        }

        public void DisposeCancellationRegistration()
        {
            CancellationRegistration.Dispose();
        }
    }

    private sealed class PendingFlush
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingFlush(long targetAcceptedFrameCount)
        {
            TargetAcceptedFrameCount = targetAcceptedFrameCount;
        }

        public long TargetAcceptedFrameCount { get; }

        public Task Task => completion.Task;

        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public void TryComplete()
        {
            DisposeCancellationRegistration();
            completion.TrySetResult();
        }

        public void TryCancel()
        {
            DisposeCancellationRegistration();
            completion.TrySetCanceled();
        }

        public void TrySetException(Exception exception)
        {
            DisposeCancellationRegistration();
            completion.TrySetException(exception);
        }

        public void DisposeCancellationRegistration()
        {
            CancellationRegistration.Dispose();
        }
    }
}
