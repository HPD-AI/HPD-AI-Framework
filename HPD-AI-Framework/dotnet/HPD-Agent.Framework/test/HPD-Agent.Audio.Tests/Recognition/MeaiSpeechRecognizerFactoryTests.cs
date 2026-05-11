// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Recognition;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Audio.Tests.Recognition;

public sealed class MeaiSpeechRecognizerFactoryTests
{
    [Fact]
    public void Create_ReturnsBatchRecognizer_WhenStreamingRequestedButCapabilitiesAreBatchOnly()
    {
        using var client = new FakeSpeechToTextClient();

        var recognizer = MeaiSpeechRecognizerFactory.Create(
            client,
            new SpeechRecognitionCapabilities
            {
                StreamingInput = false,
                InterimResults = false,
                PreflightResults = false,
                FinalResults = true
            },
            useStreamingRecognition: true);

        Assert.IsType<MeaiBatchSpeechRecognizer>(recognizer);
    }

    [Fact]
    public void Create_ReturnsStreamingRecognizer_WhenStreamingRequestedAndCapabilitiesAllow()
    {
        using var client = new FakeSpeechToTextClient();

        var recognizer = MeaiSpeechRecognizerFactory.Create(
            client,
            new SpeechRecognitionCapabilities
            {
                StreamingInput = true,
                InterimResults = true,
                PreflightResults = true,
                FinalResults = true
            },
            useStreamingRecognition: true);

        Assert.IsType<MeaiStreamingSpeechRecognizer>(recognizer);
    }

    private sealed class FakeSpeechToTextClient : ISpeechToTextClient
    {
        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SpeechToTextResponse("ok"));

        public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new SpeechToTextResponseUpdate("ok")
            {
                Kind = SpeechToTextResponseUpdateKind.TextUpdated
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
