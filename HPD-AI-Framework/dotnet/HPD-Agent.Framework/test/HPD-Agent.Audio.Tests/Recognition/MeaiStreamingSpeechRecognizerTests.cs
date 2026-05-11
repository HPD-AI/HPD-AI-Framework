// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using HPD.Agent.Audio.Recognition;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Audio.Tests.Recognition;

public sealed class MeaiStreamingSpeechRecognizerTests
{
    [Fact]
    public async Task RecognizeAsync_MapsUpdatingToPreflightAndUpdatedToFinal_WhenCapabilitiesAllow()
    {
        var client = new CapturingStreamingSpeechToTextClient(
            new SpeechToTextResponseUpdate("hello")
            {
                Kind = SpeechToTextResponseUpdateKind.TextUpdating,
                ResponseId = "response-1",
                ModelId = "provider-model"
            },
            new SpeechToTextResponseUpdate("hello world")
            {
                Kind = SpeechToTextResponseUpdateKind.TextUpdated,
                ResponseId = "response-1",
                ModelId = "provider-model"
            },
            new SpeechToTextResponseUpdate
            {
                Kind = SpeechToTextResponseUpdateKind.SessionClose,
                ResponseId = "response-1"
            });
        await using var recognizer = new MeaiStreamingSpeechRecognizer(
            client,
            new SpeechRecognitionCapabilities
            {
                StreamingInput = false,
                InterimResults = true,
                PreflightResults = true,
                FinalResults = true
            },
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
            preflight =>
            {
                var typed = Assert.IsType<SpeechRecognitionPreflightEvent>(preflight);
                Assert.Equal("hello", typed.Transcript.Text);
                Assert.Equal("response-1", typed.Context.ProviderRequestId);
                Assert.Equal("test-provider", typed.Context.Provider);
                Assert.Equal("provider-model", typed.Context.Model);
                Assert.Equal("session-override", typed.Context.SessionId);
            },
            final =>
            {
                var typed = Assert.IsType<SpeechRecognitionFinalEvent>(final);
                Assert.Equal("hello world", typed.Transcript.Text);
                Assert.Equal("en", typed.Transcript.Language);
            },
            ended => Assert.IsType<SpeechRecognitionEndedEvent>(ended));
        Assert.Equal([1, 2, 3], client.LastReceivedBytes);
        Assert.Equal(16000, client.LastOptions?.SpeechSampleRate);
        Assert.Equal("en", client.LastOptions?.SpeechLanguage);
    }

    [Fact]
    public async Task RecognizeAsync_DoesNotEmitUpdatingText_WhenInterimAndPreflightCapabilitiesAreFalse()
    {
        var client = new CapturingStreamingSpeechToTextClient(
            new SpeechToTextResponseUpdate("draft")
            {
                Kind = SpeechToTextResponseUpdateKind.TextUpdating
            },
            new SpeechToTextResponseUpdate("final")
            {
                Kind = SpeechToTextResponseUpdateKind.TextUpdated
            });
        await using var recognizer = new MeaiStreamingSpeechRecognizer(client);

        var events = new List<SpeechRecognitionEvent>();
        await foreach (var evt in recognizer.RecognizeAsync(
            GetAudioFrames(),
            new SpeechRecognitionOptions(),
            CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.DoesNotContain(events, e => e is SpeechRecognitionInterimEvent);
        Assert.DoesNotContain(events, e => e is SpeechRecognitionPreflightEvent);
        Assert.Contains(events, e => e is SpeechRecognitionFinalEvent final && final.Transcript.Text == "final");
        Assert.False(recognizer.Capabilities.StreamingInput);
        Assert.False(recognizer.Capabilities.InterimResults);
        Assert.False(recognizer.Capabilities.PreflightResults);
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

    private sealed class CapturingStreamingSpeechToTextClient(params SpeechToTextResponseUpdate[] updates)
        : ISpeechToTextClient
    {
        public byte[]? LastReceivedBytes { get; private set; }
        public SpeechToTextOptions? LastOptions { get; private set; }

        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            using var stream = new MemoryStream();
            await audioSpeechStream.CopyToAsync(stream, cancellationToken);
            LastReceivedBytes = stream.ToArray();

            foreach (var update in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
