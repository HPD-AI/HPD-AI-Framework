// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Recognition;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Audio.Tests.Recognition;

public sealed class BatchSttWithVadRecognizerTests
{
    [Fact]
    public async Task RecognizeAsync_EmitsVadBoundariesThenFinalTranscript()
    {
        var stt = new CapturingSpeechToTextClient("hello after vad");
        using var vad = new ScriptedVad(
            new VadResult { State = VadState.Starting, SpeechProbability = 0.8f, IsSpeaking = true },
            new VadResult { State = VadState.Speaking, SpeechProbability = 0.9f, IsSpeaking = true },
            new VadResult { State = VadState.Stopping, SpeechProbability = 0.2f, IsSpeaking = false });
        await using var recognizer = new BatchSttWithVadRecognizer(
            stt,
            vad,
            provider: "test-provider",
            model: "test-model");

        var events = new List<SpeechRecognitionEvent>();
        await foreach (var evt in recognizer.RecognizeAsync(
            GetAudioFrames(),
            new SpeechRecognitionOptions
            {
                RuntimeId = "runtime-1",
                SessionId = "session-1",
                BranchId = "main",
                Language = "en",
                SampleRate = 16000
            }))
        {
            events.Add(evt);
        }

        Assert.Collection(
            events,
            started =>
            {
                var typed = Assert.IsType<SpeechRecognitionStartedEvent>(started);
                Assert.Equal(0.8f, typed.SpeechProbability);
                Assert.Equal("test-provider", typed.Context.Provider);
            },
            ended =>
            {
                var typed = Assert.IsType<SpeechRecognitionEndedEvent>(ended);
                Assert.Equal(TimeSpan.FromTicks(20_000), typed.SpeechDuration);
            },
            final =>
            {
                var typed = Assert.IsType<SpeechRecognitionFinalEvent>(final);
                Assert.Equal("hello after vad", typed.Transcript.Text);
                Assert.Equal("en", typed.Transcript.Language);
            });
        Assert.Equal([1, 2, 3], stt.LastReceivedBytes);
        Assert.Equal(3, vad.ProcessCount);
    }

    [Fact]
    public void Capabilities_AreVadStreamingButTranscriptBatchBacked()
    {
        using var stt = new CapturingSpeechToTextClient("hello");
        using var vad = new ScriptedVad();
        var recognizer = new BatchSttWithVadRecognizer(stt, vad);

        Assert.True(recognizer.Capabilities.StreamingInput);
        Assert.False(recognizer.Capabilities.InterimResults);
        Assert.False(recognizer.Capabilities.PreflightResults);
        Assert.True(recognizer.Capabilities.FinalResults);
    }

    private static async IAsyncEnumerable<AudioInputFrame> GetAudioFrames()
    {
        yield return new AudioInputFrame("session-1", "main", new byte[] { 1 }, "audio/pcm", 0, false, 1);
        await Task.Yield();
        yield return new AudioInputFrame("session-1", "main", new byte[] { 2 }, "audio/pcm", 1_000_000, false, 2);
        yield return new AudioInputFrame("session-1", "main", new byte[] { 3 }, "audio/pcm", 2_000_000, true, 3);
    }

    private sealed class ScriptedVad(params VadResult[] results) : IVoiceActivityDetector
    {
        private int _index;

        public int ProcessCount { get; private set; }

        public VadResult Process(AudioFrame frame)
        {
            ProcessCount++;
            if (results.Length == 0)
                return default;

            var index = Math.Min(_index++, results.Length - 1);
            return results[index];
        }

        public async IAsyncEnumerable<VadEvent> DetectAsync(
            IAsyncEnumerable<AudioFrame> audio,
            CancellationToken cancellationToken = default)
        {
            await foreach (var _ in audio.WithCancellation(cancellationToken))
            {
                var result = Process(default);
                yield return new VadEvent
                {
                    Type = result.IsSpeaking ? VadEventType.StartOfSpeech : VadEventType.InferenceDone,
                    Timestamp = TimeSpan.Zero,
                    SpeechProbability = result.SpeechProbability
                };
            }
        }

        public void Reset() => _index = 0;

        public void Dispose()
        {
        }
    }

    private sealed class CapturingSpeechToTextClient(string result) : ISpeechToTextClient
    {
        public byte[]? LastReceivedBytes { get; private set; }

        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var stream = new MemoryStream();
            audioSpeechStream.CopyTo(stream);
            LastReceivedBytes = stream.ToArray();
            return Task.FromResult(new SpeechToTextResponse(result));
        }

        public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
