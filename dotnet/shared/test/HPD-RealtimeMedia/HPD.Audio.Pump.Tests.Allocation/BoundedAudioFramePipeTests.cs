#nullable enable

using HPD.Audio.Primitives;

namespace HPD.Audio.Pump.Tests.Allocation;

public sealed class BoundedAudioFramePipeTests
{
    private static readonly AudioFormat Format = new()
    {
        SampleRate = 16000,
        ChannelCount = 1,
        SampleFormat = AudioSampleFormat.Pcm16
    };

    [Fact]
    public void TryWriteTryRead_PreservesFrameOrder()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 2);
        byte[] firstBytes = [1, 2];
        byte[] secondBytes = [3, 4];
        var first = CreateFrame(firstBytes, sequenceNumber: 1);
        var second = CreateFrame(secondBytes, sequenceNumber: 2);

        Assert.True(pipe.TryWrite(first));
        Assert.True(pipe.TryWrite(second));
        Assert.True(pipe.TryRead(out AudioFrame firstRead));
        Assert.True(pipe.TryRead(out AudioFrame secondRead));

        Assert.Equal(1, firstRead.SequenceNumber);
        Assert.Equal(2, secondRead.SequenceNumber);
        Assert.True(firstRead.Data.Span.SequenceEqual(firstBytes));
        Assert.True(secondRead.Data.Span.SequenceEqual(secondBytes));
    }

    [Fact]
    public void TryWrite_ReturnsFalseWhenFull()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);

        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 1)));
        Assert.False(pipe.TryWrite(CreateFrame([3, 4], sequenceNumber: 2)));
    }

    [Fact]
    public void Constructor_RejectsUnusableFormat()
    {
        Assert.Throws<ArgumentException>(() => new BoundedAudioFramePipe(Format with { SampleRate = 0 }, capacity: 1));
        Assert.Throws<ArgumentException>(() => new BoundedAudioFramePipe(Format with { ChannelCount = 0 }, capacity: 1));
        Assert.Throws<ArgumentException>(() => new BoundedAudioFramePipe(Format with { SampleFormat = 0 }, capacity: 1));
    }

    [Fact]
    public async Task CompleteAsync_DrainsThenCompletesSource()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 1)));

        ValueTask complete = pipe.CompleteAsync();

        Assert.False(complete.IsCompleted);
        Assert.Equal(AudioSinkState.Completing, ((IAudioSink)pipe).State);
        AudioReadResult first = await pipe.ReadAsync();
        await complete;
        AudioReadResult second = await pipe.ReadAsync();

        Assert.True(first.HasFrame);
        Assert.False(second.HasFrame);
        Assert.Equal(AudioSourceState.Completed, pipe.State);
        Assert.Equal(AudioSinkState.Completed, ((IAudioSink)pipe).State);
    }

    [Fact]
    public async Task FlushAsync_WaitsUntilBufferedAcceptedFramesDrain()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 1)));

        ValueTask flush = pipe.FlushAsync();

        Assert.False(flush.IsCompleted);
        Assert.True(pipe.TryRead(out AudioFrame frame));
        await flush;

        Assert.Equal(1, frame.SequenceNumber);
        Assert.Equal(AudioSinkState.Open, ((IAudioSink)pipe).State);
        Assert.True(pipe.TryWrite(CreateFrame([3, 4], sequenceNumber: 2)));
    }

    [Fact]
    public async Task FlushAsync_DoesNotWaitForBackpressuredPendingWrite()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 1)));
        ValueTask pendingWrite = pipe.WriteAsync(CreateFrame([3, 4], sequenceNumber: 2));

        ValueTask flush = pipe.FlushAsync();

        Assert.False(flush.IsCompleted);
        Assert.True(pipe.TryRead(out AudioFrame first));
        await flush;
        await pendingWrite;
        Assert.True(pipe.TryRead(out AudioFrame second));

        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(2, second.SequenceNumber);
    }

    [Fact]
    public async Task DisposeAsync_FailsPendingFlush()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 1)));
        ValueTask flush = pipe.FlushAsync();

        await pipe.DisposeAsync();
        AudioSinkException exception = await Assert.ThrowsAsync<AudioSinkException>(async () => await flush);

        Assert.Equal(AudioStreamErrorKind.Disposed, exception.Kind);
    }

    [Fact]
    public async Task ReadAsync_WaitsUntilFrameArrivesWhileSourceIsOpen()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        ValueTask<AudioReadResult> pending = pipe.ReadAsync();

        Assert.False(pending.IsCompleted);
        Assert.Equal(AudioSourceState.Open, pipe.State);
        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 7)));
        AudioReadResult result = await pending;

        Assert.True(result.HasFrame);
        Assert.False(result.IsCompleted);
        Assert.Equal(7, result.Frame.SequenceNumber);
    }

    [Fact]
    public async Task FlushAsync_CompletesAfterTryWriteDirectReaderHandoff()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        ValueTask<AudioReadResult> pending = pipe.ReadAsync();

        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 7)));
        AudioReadResult result = await pending;
        ValueTask flush = pipe.FlushAsync();

        Assert.True(result.HasFrame);
        Assert.Equal(7, result.Frame.SequenceNumber);
        Assert.True(flush.IsCompletedSuccessfully);
        await flush;
    }

    [Fact]
    public async Task FlushAsync_CompletesAfterWriteAsyncDirectReaderHandoff()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        ValueTask<AudioReadResult> pending = pipe.ReadAsync();

        await pipe.WriteAsync(CreateFrame([1, 2], sequenceNumber: 8));
        AudioReadResult result = await pending;
        ValueTask flush = pipe.FlushAsync();

        Assert.True(result.HasFrame);
        Assert.Equal(8, result.Frame.SequenceNumber);
        Assert.True(flush.IsCompletedSuccessfully);
        await flush;
    }

    [Fact]
    public async Task ReadAsync_PendingReadCompletesWhenSourceCompletes()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        ValueTask<AudioReadResult> pending = pipe.ReadAsync();

        Assert.False(pending.IsCompleted);
        await pipe.CompleteAsync();
        AudioReadResult result = await pending;

        Assert.False(result.HasFrame);
        Assert.True(result.IsCompleted);
        Assert.Equal(AudioSourceState.Completed, pipe.State);
    }

    [Fact]
    public async Task DisposeAsync_FailsPendingRead()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        ValueTask<AudioReadResult> pending = pipe.ReadAsync();

        Assert.False(pending.IsCompleted);
        await pipe.DisposeAsync();
        AudioSourceException exception = await Assert.ThrowsAsync<AudioSourceException>(async () => await pending);

        Assert.Equal(AudioStreamErrorKind.Disposed, exception.Kind);
        Assert.Equal(AudioSourceState.Disposed, pipe.State);
    }

    [Fact]
    public async Task ReadAsync_AfterDisposeThrowsDisposed()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);

        await pipe.DisposeAsync();
        AudioSourceException exception = await Assert.ThrowsAsync<AudioSourceException>(async () => await pipe.ReadAsync());

        Assert.Equal(AudioStreamErrorKind.Disposed, exception.Kind);
    }

    [Fact]
    public async Task ReadAsync_CanceledPendingReadDoesNotConsumeNextFrame()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        using var cancellation = new CancellationTokenSource();
        ValueTask<AudioReadResult> pending = pipe.ReadAsync(cancellation.Token);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 9)));

        Assert.True(pipe.TryRead(out AudioFrame frame));
        Assert.Equal(9, frame.SequenceNumber);
    }

    [Fact]
    public async Task WriteAsync_WaitsForCapacityWhenFull()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 1)));

        ValueTask pending = pipe.WriteAsync(CreateFrame([3, 4], sequenceNumber: 2));

        Assert.False(pending.IsCompleted);
        Assert.True(pipe.TryRead(out AudioFrame first));
        await pending;
        Assert.True(pipe.TryRead(out AudioFrame second));

        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(2, second.SequenceNumber);
    }

    [Fact]
    public async Task WriteAsync_PreservesPendingWriterOrder()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 1)));

        ValueTask secondPending = pipe.WriteAsync(CreateFrame([3, 4], sequenceNumber: 2));
        ValueTask thirdPending = pipe.WriteAsync(CreateFrame([5, 6], sequenceNumber: 3));

        Assert.False(secondPending.IsCompleted);
        Assert.False(thirdPending.IsCompleted);
        Assert.False(pipe.TryWrite(CreateFrame([7, 8], sequenceNumber: 4)));

        Assert.True(pipe.TryRead(out AudioFrame first));
        await secondPending;
        Assert.False(thirdPending.IsCompleted);
        Assert.True(pipe.TryRead(out AudioFrame second));
        await thirdPending;
        Assert.True(pipe.TryRead(out AudioFrame third));

        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(2, second.SequenceNumber);
        Assert.Equal(3, third.SequenceNumber);
    }

    [Fact]
    public async Task WriteAsync_CanceledPendingWriteDoesNotEnqueueFrame()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 1)));
        using var cancellation = new CancellationTokenSource();
        ValueTask pending = pipe.WriteAsync(CreateFrame([3, 4], sequenceNumber: 2), cancellation.Token);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.True(pipe.TryRead(out AudioFrame first));
        Assert.False(pipe.TryRead(out _));
        Assert.True(pipe.TryWrite(CreateFrame([5, 6], sequenceNumber: 3)));
        Assert.True(pipe.TryRead(out AudioFrame third));

        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(3, third.SequenceNumber);
    }

    [Fact]
    public async Task WriteAsync_CanceledPendingWriteDoesNotBlockLaterFastWrite()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 2);
        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 1)));
        Assert.True(pipe.TryWrite(CreateFrame([3, 4], sequenceNumber: 2)));
        using var cancellation = new CancellationTokenSource();
        ValueTask pending = pipe.WriteAsync(CreateFrame([5, 6], sequenceNumber: 3), cancellation.Token);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.True(pipe.TryRead(out AudioFrame first));
        Assert.True(pipe.TryWrite(CreateFrame([7, 8], sequenceNumber: 4)));
        Assert.True(pipe.TryRead(out AudioFrame second));
        Assert.True(pipe.TryRead(out AudioFrame fourth));
        Assert.False(pipe.TryRead(out _));

        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(2, second.SequenceNumber);
        Assert.Equal(4, fourth.SequenceNumber);
    }

    [Fact]
    public async Task CompleteAsync_FailsPendingWrite()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 1)));
        ValueTask pending = pipe.WriteAsync(CreateFrame([3, 4], sequenceNumber: 2));

        ValueTask complete = pipe.CompleteAsync();
        AudioSinkException exception = await Assert.ThrowsAsync<AudioSinkException>(async () => await pending);
        Assert.True(pipe.TryRead(out AudioFrame accepted));
        await complete;

        Assert.Equal(AudioStreamErrorKind.AlreadyCompleted, exception.Kind);
        Assert.Equal(1, accepted.SequenceNumber);
    }

    [Fact]
    public async Task DisposeAsync_FailsPendingWrite()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        Assert.True(pipe.TryWrite(CreateFrame([1, 2], sequenceNumber: 1)));
        ValueTask pending = pipe.WriteAsync(CreateFrame([3, 4], sequenceNumber: 2));

        await pipe.DisposeAsync();
        AudioSinkException exception = await Assert.ThrowsAsync<AudioSinkException>(async () => await pending);

        Assert.Equal(AudioStreamErrorKind.Disposed, exception.Kind);
    }

    [Fact]
    public async Task WriteAsync_ThrowsFormatMismatch()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        var mismatched = new AudioFrame
        {
            Data = new byte[2],
            Format = new AudioFormat
            {
                SampleRate = 8000,
                ChannelCount = Format.ChannelCount,
                SampleFormat = Format.SampleFormat
            },
            SamplesPerChannel = 1
        };

        AudioSinkException exception = await Assert.ThrowsAsync<AudioSinkException>(async () => await pipe.WriteAsync(mismatched));
        Assert.Equal(AudioStreamErrorKind.FormatMismatch, exception.Kind);
    }

    [Fact]
    public async Task WriteAsync_ThrowsFormatMismatchForMalformedPcmFrame()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        var zeroSamples = CreateFrame([1, 2], sequenceNumber: 1) with { SamplesPerChannel = 0 };
        var byteLengthMismatch = CreateFrame([1, 2, 3], sequenceNumber: 2) with { SamplesPerChannel = 1 };

        AudioSinkException zeroSamplesException = await Assert.ThrowsAsync<AudioSinkException>(async () => await pipe.WriteAsync(zeroSamples));
        AudioSinkException byteLengthException = await Assert.ThrowsAsync<AudioSinkException>(async () => await pipe.WriteAsync(byteLengthMismatch));

        Assert.Equal(AudioStreamErrorKind.FormatMismatch, zeroSamplesException.Kind);
        Assert.Equal(AudioStreamErrorKind.FormatMismatch, byteLengthException.Kind);
    }

    [Fact]
    public void TryWrite_ReturnsFalseForMalformedPcmFrame()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        var zeroSamples = CreateFrame([1, 2], sequenceNumber: 1) with { SamplesPerChannel = 0 };
        var byteLengthMismatch = CreateFrame([1, 2, 3], sequenceNumber: 2) with { SamplesPerChannel = 1 };

        Assert.False(pipe.TryWrite(zeroSamples));
        Assert.False(pipe.TryWrite(byteLengthMismatch));
        Assert.False(pipe.TryRead(out _));
    }

    [Fact]
    public void TryWriteTryRead_DoesNotAllocateAfterWarmup()
    {
        var pipe = new BoundedAudioFramePipe(Format, capacity: 1);
        byte[] payload = new byte[320];
        var frame = CreateFrame(payload, sequenceNumber: 1);

        for (int i = 0; i < 1_000; i++)
        {
            if (!pipe.TryWrite(frame) || !pipe.TryRead(out _))
            {
                throw new InvalidOperationException("Audio frame pipe failed during warmup.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (!pipe.TryWrite(frame) || !pipe.TryRead(out _))
            {
                throw new InvalidOperationException("Audio frame pipe failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    private static AudioFrame CreateFrame(byte[] data, long sequenceNumber)
    {
        return new AudioFrame
        {
            Data = data,
            Format = Format,
            SamplesPerChannel = data.Length / 2,
            SequenceNumber = sequenceNumber
        };
    }
}
