// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Audio.Tests;

public class AudioRunConfigStreamingTests
{
    [Fact]
    public async Task WrapModelCallStreamingAsync_AudioRunConfigVoice_ReachesTtsOptions()
    {
        var tts = new FakeTextToSpeechClient();
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio,
            DefaultVoice = "alloy",
            DefaultModel = "tts-1"
        };

        var request = new ModelRequest
        {
            Model = new SingleResponseChatClient("Hello."),
            Messages = [new ChatMessage(ChatRole.User, "test")],
            Options = new ChatOptions(),
            State = AgentLoopState.InitialSafe([], "run", "conv", "TestAgent"),
            Iteration = 0,
            RunConfig = new AgentRunConfig().WithAudio(audio =>
            {
                audio.Voice = "nova";
                audio.TtsModel = "tts-1-hd";
                audio.TtsSpeed = 1.25f;
            })
        };

        await DrainAsync(middleware.WrapModelCallStreamingAsync(
            request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options),
            CancellationToken.None)!);

        var synthesis = Assert.Single(tts.Requests);
        Assert.Equal("nova", synthesis.Options?.VoiceId);
        Assert.Equal("tts-1-hd", synthesis.Options?.ModelId);
        Assert.Equal(1.25f, synthesis.Options?.Speed);
    }

    private static async Task DrainAsync(IAsyncEnumerable<ChatResponseUpdate> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    private sealed class SingleResponseChatClient(string response) : IChatClient
    {
        public ChatClientMetadata Metadata => new("fake");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(response)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
