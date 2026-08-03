using System.Runtime.CompilerServices;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

#pragma warning disable MEAI001

public sealed class RealtimeConsumerSetupTests
{
    [Fact]
    public async Task AgentBuilder_WithRunConfigRealtimeClient_RunsPackageStyleRealtimeTurn()
    {
        var realtimeSession = new ConsumerRealtimeSession(
            [
                new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDelta)
                {
                    ResponseId = "resp-consumer",
                    Text = "Realtime answer."
                },
                new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDone)
                {
                    ResponseId = "resp-consumer"
                }
            ]);
        var store = new InMemorySessionStore();
        var agent = await AgentBuilder
            .Create()
            .WithDeferredProvider()
            .WithSessionStore(store)
            .BuildAsync();

        try
        {
            await agent.CreateSessionAsync("consumer-realtime-session");

            var userMessage = new ChatMessage(
                ChatRole.User,
                [
                    new TextContent("Answer this audio."),
                    AudioContent.Pcm(new byte[] { 1, 2, 3, 4 }, sampleRate: 16000)
                ])
            {
                MessageId = "consumer-audio-message"
            };

            await agent.RunAsync(
                new UserMessagesInputEvent { Messages = [userMessage],
                    SessionId = "consumer-realtime-session",
                    ThreadId = "main",
                    RunConfig = new AgentRunConfig
                    {
                        Clients = new AgentClientsConfig
                        {
                            Transport = AgentModelTransportMode.Realtime,
                            Realtime = new RealtimeClientConfig
                            {
                                Override = new ClientOverride<IRealtimeClient>
                                {
                                    Client = new ConsumerRealtimeClient(realtimeSession)
                                }
                            }
                        }
                    }
                });
        }
        finally
        {
            agent.Dispose();
        }

        Assert.NotNull(realtimeSession.Options?.InputAudioFormat);
        Assert.Equal("audio/pcm", realtimeSession.Options.InputAudioFormat.MediaType);
        Assert.Equal(16000, realtimeSession.Options.InputAudioFormat.SampleRate);

        Assert.Collection(
            realtimeSession.Sent,
            message =>
            {
                var append = Assert.IsType<InputAudioBufferAppendRealtimeClientMessage>(message);
                Assert.Equal([1, 2, 3, 4], append.Content.Data.ToArray());
            },
            message => Assert.IsType<InputAudioBufferCommitRealtimeClientMessage>(message),
            message =>
            {
                var createResponse = Assert.IsType<CreateResponseRealtimeClientMessage>(message);
                var item = Assert.Single(createResponse.Items!);
                Assert.Equal(ChatRole.User, item.Role);
                Assert.Equal("Answer this audio.", Assert.Single(item.Contents.OfType<TextContent>()).Text);
                Assert.Empty(item.Contents.OfType<AudioContent>());
            });

        var thread = await store.ProjectThreadAsync("consumer-realtime-session", "main", ThreadProjectionPurpose.ThreadHistory);
        Assert.NotNull(thread);
        Assert.Equal("Answer this audio.", thread.Messages[0].Text);
        Assert.Equal("Realtime answer.", thread.Messages[^1].Text);
    }

    private sealed class ConsumerRealtimeClient(ConsumerRealtimeSession session) : IRealtimeClient
    {
        public Task<IRealtimeClientSession> CreateSessionAsync(
            RealtimeSessionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.Options = options;
            return Task.FromResult<IRealtimeClientSession>(session);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class ConsumerRealtimeSession(
        IReadOnlyList<RealtimeServerMessage> responses) : IRealtimeClientSession
    {
        private bool _streamed;

        public RealtimeSessionOptions? Options { get; set; }

        public List<RealtimeClientMessage> Sent { get; } = [];

        public Task SendAsync(
            RealtimeClientMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_streamed)
                yield break;

            _streamed = true;
            foreach (var response in responses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return response;
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }
}

#pragma warning restore MEAI001
