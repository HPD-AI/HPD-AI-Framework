using Microsoft.Extensions.AI;
using Xunit;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Tests.Session;

/// <summary>Exercises the public run/compact/run path across durable thread loading.</summary>
public sealed class PostCompactionContinuationTests
{
    [Theory]
    [InlineData(CompactionCommitMode.Soft, false)]
    [InlineData(CompactionCommitMode.Soft, true)]
    [InlineData(CompactionCommitMode.Hard, false)]
    [InlineData(CompactionCommitMode.Hard, true)]
    public async Task NextRunReceivesSummaryAfterCompaction(CompactionCommitMode mode, bool recreateAgent)
    {
        var store = new InMemorySessionStore(TestEventApplication.Codec);
        var client = new CaptureClient();
        async Task<Agent> Build(string name = "continuation-test") => await new AgentBuilder(new AgentConfig
        {
            Name = name, AgentId = name, Compaction = new CompactionConfig(),
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig() }
        }).WithChatClient(client).WithSessionStore(store)
            .WithEventComposition(TestEventApplication.Composition).BuildAsync();
        var agent = await Build();
        await agent.CreateSessionAsync("continuation-session");
        await agent.RunAsync("Remember the marker ORCHID-739; only inspect.", "continuation-session", "main");
        await agent.RunAsync(new CompactThreadInputEvent
        {
            SessionId = "continuation-session", ThreadId = "main",
            Request = new ThreadCompactionRequest
            {
                Compaction = new CompactionSpecification
                {
                    Point = new CompactAtCurrentHead(), CommitMode = mode,
                    Strategy = new SummarizingCompaction()
                }
            }
        });
        if (recreateAgent)
        {
            await agent.DisposeAsync();
            agent = await Build("replacement-agent");
        }
        await agent.RunAsync("What do you remember?", "continuation-session", "main");
        var request = client.Requests.Last();
        Assert.Contains(request, m => m.Role == ChatRole.Assistant && m.Text == CaptureClient.Summary);
        Assert.Contains(request, m => m.Role == ChatRole.User && m.Text == "What do you remember?");
        Assert.DoesNotContain(request, m => m.Text == "Remember the marker ORCHID-739; only inspect.");
        await agent.DisposeAsync();
    }

    private sealed class CaptureClient : IChatClient
    {
        internal const string Summary = "## Goal\nInspection complete.\n## Important facts\nUser marker: ORCHID-739. No edits made.\n## Remaining work\n(none)";
        internal List<ChatMessage[]> Requests { get; } = [];
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Summary)) { FinishReason = ChatFinishReason.Stop });
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToArray());
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Recorded.") { FinishReason = ChatFinishReason.Stop };
            await Task.CompletedTask;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(HPD.Agent.Providers.ProviderClientExecutionIdentity)
                ? HPD.Agent.Providers.ProviderClientExecutionIdentity.CreateSafe("test", "platform",
                    HPD.Agent.Providers.ProviderClientFamily.Chat, "test", "test-chat", "test-usage") : null;
        public void Dispose() { }
    }
}
