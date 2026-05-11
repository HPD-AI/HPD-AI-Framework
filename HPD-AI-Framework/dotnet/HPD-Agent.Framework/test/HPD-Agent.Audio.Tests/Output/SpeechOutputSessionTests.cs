// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Output;
using Xunit;

namespace HPD.Agent.Audio.Tests.Output;

public sealed class SpeechOutputSessionTests
{
    [Fact]
    public async Task PushAudioAsync_EmitsStartedAndAudioQueued()
    {
        await using var session = new SpeechOutputSession(
            speechId: "speech-1",
            streamId: "stream-1",
            synthesisId: "synth-1",
            model: "tts-1",
            voice: "nova");

        var frame = new AudioChunkFrame(
            SynthesisId: "synth-1",
            Audio: new byte[] { 1, 2, 3 },
            MimeType: "audio/mpeg",
            ChunkIndex: 0,
            Duration: TimeSpan.FromMilliseconds(100),
            IsLast: false,
            TimestampNs: 123,
            SequenceNumber: 9);

        await session.PushAudioAsync(frame);
        await session.FlushAsync();

        var events = await CollectAsync(session.Events);

        Assert.Collection(
            events,
            evt => Assert.IsType<SpeechOutputStartedEvent>(evt),
            evt =>
            {
                var queued = Assert.IsType<SpeechOutputAudioQueuedEvent>(evt);
                Assert.Equal(frame, queued.Frame);
                Assert.Equal(TimeSpan.FromMilliseconds(100), queued.State.GeneratedDuration);
                Assert.Equal(1, queued.State.QueuedChunks);
                Assert.Equal(9, queued.Context.SequenceNumber);
            },
            evt =>
            {
                var completed = Assert.IsType<SpeechOutputCompletedEvent>(evt);
                Assert.Equal(1, completed.State.QueuedChunks);
            });
    }

    [Fact]
    public async Task InterruptAsync_MarksOutputInterrupted()
    {
        await using var session = new SpeechOutputSession("speech-1", "stream-1");

        await session.PushTextAsync("hello");
        await session.InterruptAsync();

        var events = await CollectAsync(session.Events);
        var interrupted = Assert.IsType<SpeechOutputInterruptedEvent>(events[^1]);
        Assert.True(interrupted.State.Interrupted);
        Assert.Equal("interrupt_requested", interrupted.Reason);
    }

    [Fact]
    public async Task PlaybackProgressAsync_TracksPlayedDurationSeparatelyFromQueuedDuration()
    {
        await using var session = new SpeechOutputSession("speech-1", "stream-1");

        var frame = new AudioChunkFrame(
            SynthesisId: "synth-1",
            Audio: new byte[] { 1 },
            MimeType: "audio/mpeg",
            ChunkIndex: 0,
            Duration: TimeSpan.FromMilliseconds(250),
            IsLast: false,
            TimestampNs: 0);

        await session.PushAudioAsync(frame);
        Assert.Equal(TimeSpan.FromMilliseconds(250), session.State.QueuedDuration);
        Assert.Equal(TimeSpan.Zero, session.State.PlayedDuration);

        await session.MarkPlaybackProgressAsync(
            playedDuration: TimeSpan.FromMilliseconds(100),
            playbackPosition: TimeSpan.FromMilliseconds(100));
        await session.FlushAsync();

        var events = await CollectAsync(session.Events);
        var progress = Assert.IsType<SpeechOutputPlaybackProgressEvent>(
            events.Single(e => e is SpeechOutputPlaybackProgressEvent));

        Assert.Contains(events, e => e is SpeechOutputPlaybackStartedEvent);
        Assert.Equal(TimeSpan.FromMilliseconds(100), progress.State.PlayedDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(250), progress.State.QueuedDuration);
        Assert.Equal(1, progress.State.PlayedChunks);
    }

    [Fact]
    public async Task InterruptAsync_RecordsGeneratedButUnplayedDurationAsDiscarded()
    {
        await using var session = new SpeechOutputSession("speech-1", "stream-1");

        var frame = new AudioChunkFrame(
            SynthesisId: "synth-1",
            Audio: new byte[] { 1 },
            MimeType: "audio/mpeg",
            ChunkIndex: 0,
            Duration: TimeSpan.FromMilliseconds(250),
            IsLast: false,
            TimestampNs: 0);

        await session.PushAudioAsync(frame);
        await session.MarkPlaybackProgressAsync(
            playedDuration: TimeSpan.FromMilliseconds(100),
            playbackPosition: TimeSpan.FromMilliseconds(100));
        await session.InterruptAsync();

        var events = await CollectAsync(session.Events);
        var interrupted = Assert.IsType<SpeechOutputInterruptedEvent>(events[^1]);

        Assert.Equal(TimeSpan.FromMilliseconds(100), interrupted.State.PlayedDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(150), interrupted.State.DiscardedDuration);
    }

    [Fact]
    public async Task PauseAsync_HoldsGeneratedButUnplayedAudioUntilResume()
    {
        await using var session = new SpeechOutputSession("speech-1", "stream-1");

        var first = new AudioChunkFrame(
            SynthesisId: "synth-1",
            Audio: new byte[] { 1 },
            MimeType: "audio/mpeg",
            ChunkIndex: 0,
            Duration: TimeSpan.FromMilliseconds(100),
            IsLast: false,
            TimestampNs: 0);
        var second = first with
        {
            ChunkIndex = 1,
            Duration = TimeSpan.FromMilliseconds(200)
        };

        await session.PushAudioAsync(first);
        await session.MarkPlaybackProgressAsync(
            playedDuration: TimeSpan.FromMilliseconds(100),
            playbackPosition: TimeSpan.FromMilliseconds(100));
        await session.PauseAsync();
        await session.PushAudioAsync(second);

        Assert.True(session.State.IsPaused);
        Assert.Equal(TimeSpan.FromMilliseconds(200), session.State.HeldDuration);
        Assert.Equal(1, session.State.HeldChunks);

        await session.ResumeAsync();
        await session.FlushAsync();

        var events = await CollectAsync(session.Events);
        var resumed = Assert.IsType<SpeechOutputResumedEvent>(
            events.Single(e => e is SpeechOutputResumedEvent));

        Assert.False(resumed.State.IsPaused);
        Assert.Equal(TimeSpan.Zero, resumed.State.HeldDuration);
        Assert.Equal(0, resumed.State.HeldChunks);
    }

    [Fact]
    public async Task InterruptAsync_DiscardsHeldAudio()
    {
        await using var session = new SpeechOutputSession("speech-1", "stream-1");

        var first = new AudioChunkFrame(
            SynthesisId: "synth-1",
            Audio: new byte[] { 1 },
            MimeType: "audio/mpeg",
            ChunkIndex: 0,
            Duration: TimeSpan.FromMilliseconds(100),
            IsLast: false,
            TimestampNs: 0);
        var second = first with
        {
            ChunkIndex = 1,
            Duration = TimeSpan.FromMilliseconds(200)
        };

        await session.PushAudioAsync(first);
        await session.MarkPlaybackProgressAsync(
            playedDuration: TimeSpan.FromMilliseconds(100),
            playbackPosition: TimeSpan.FromMilliseconds(100));
        await session.PauseAsync();
        await session.PushAudioAsync(second);
        await session.InterruptAsync();

        var events = await CollectAsync(session.Events);
        var interrupted = Assert.IsType<SpeechOutputInterruptedEvent>(events[^1]);

        Assert.True(interrupted.State.Interrupted);
        Assert.False(interrupted.State.IsPaused);
        Assert.Equal(TimeSpan.FromMilliseconds(200), interrupted.State.DiscardedDuration);
    }

    private static async Task<List<SpeechOutputEvent>> CollectAsync(IAsyncEnumerable<SpeechOutputEvent> events)
    {
        var list = new List<SpeechOutputEvent>();
        await foreach (var evt in events)
            list.Add(evt);
        return list;
    }
}
