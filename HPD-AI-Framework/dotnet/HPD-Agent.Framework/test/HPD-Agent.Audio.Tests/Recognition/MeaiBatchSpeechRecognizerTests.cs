// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Recognition;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Audio.Tests.Recognition;

public sealed class MeaiBatchSpeechRecognizerTests
{
    [Fact]
    public async Task RecognizeAsync_BuffersFramesAndEmitsStartedFinalEnded()
    {
        var client = new CapturingSpeechToTextClient("hello from recognizer");
        await using var recognizer = new MeaiBatchSpeechRecognizer(
            client,
            provider: "test-provider",
            model: "test-model");

        var events = new List<SpeechRecognitionEvent>();
        await foreach (var evt in recognizer.RecognizeAsync(
            GetAudioFrames(),
            new SpeechRecognitionOptions
            {
                RuntimeId = "runtime-1",
                SessionId = "session-override",
                Language = "en",
                SampleRate = 16000
            },
            CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Collection(
            events,
            started => Assert.IsType<SpeechRecognitionStartedEvent>(started),
            final =>
            {
                var typed = Assert.IsType<SpeechRecognitionFinalEvent>(final);
                Assert.Equal("hello from recognizer", typed.Transcript.Text);
                Assert.Equal("en", typed.Transcript.Language);
                Assert.Equal("test-provider", typed.Context.Provider);
                Assert.Equal("test-model", typed.Context.Model);
                Assert.Equal("session-override", typed.Context.SessionId);
            },
            ended => Assert.IsType<SpeechRecognitionEndedEvent>(ended));
        Assert.Equal([1, 2, 3], client.LastReceivedBytes);
        Assert.Equal(16000, client.LastOptions?.SpeechSampleRate);
        Assert.Equal("en", client.LastOptions?.SpeechLanguage);
    }

    [Fact]
    public void Capabilities_AreBatchHonest()
    {
        using var client = new CapturingSpeechToTextClient("hello");
        var recognizer = new MeaiBatchSpeechRecognizer(client);

        Assert.False(recognizer.Capabilities.StreamingInput);
        Assert.False(recognizer.Capabilities.InterimResults);
        Assert.False(recognizer.Capabilities.PreflightResults);
        Assert.True(recognizer.Capabilities.FinalResults);
    }

    private static async IAsyncEnumerable<AudioInputFrame> GetAudioFrames()
    {
        yield return new AudioInputFrame(
            SessionId: "session-1",
            BranchId: "main",
            Audio: new byte[] { 1, 2 },
            MimeType: "audio/pcm",
            TimestampNs: 1_000_000,
            IsFinal: false,
            SequenceNumber: 1);
        await Task.Yield();
        yield return new AudioInputFrame(
            SessionId: "session-1",
            BranchId: "main",
            Audio: new byte[] { 3 },
            MimeType: "audio/pcm",
            TimestampNs: 2_000_000,
            IsFinal: true,
            SequenceNumber: 2);
    }

    private sealed class CapturingSpeechToTextClient(string result) : ISpeechToTextClient
    {
        public byte[]? LastReceivedBytes { get; private set; }
        public SpeechToTextOptions? LastOptions { get; private set; }

        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
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
