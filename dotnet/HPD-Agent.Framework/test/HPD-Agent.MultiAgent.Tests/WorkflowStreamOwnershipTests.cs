using System.Runtime.CompilerServices;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.MultiAgent;
using Microsoft.Extensions.AI;

namespace HPD.MultiAgent.Tests;

public sealed class WorkflowStreamOwnershipTests
{
    [Fact]
    public async Task CompletedGraph_DrainsNodeAndWorkflowTerminalEvents()
    {
        using var client = new StreamingClient(false);
        await using var agent = await CreateAgent(client);
        var workflow = await AgentWorkflow.Create().AddAgent("reviewer", agent).BuildAsync();
        var events = new List<HPD.Events.Event>();
        await foreach (var evt in workflow.ExecuteStreamingAsync("Review")) events.Add(evt);
        Assert.Single(events.OfType<WorkflowAgentCompletedEvent>(), e => e.AgentId == "reviewer" && e.Success);
        Assert.Single(events.OfType<WorkflowCompletedEvent>(), e => e.Success);
        Assert.Contains(events.OfType<TextDeltaEvent>(), e => e.Text.Contains("Started"));
    }

    [Fact]
    public async Task DisposingStream_CancelsAndDrainsOwnedNodeWork()
    {
        using var client = new StreamingClient(true);
        await using var agent = await CreateAgent(client);
        var workflow = await AgentWorkflow.Create().AddAgent("reviewer", agent).BuildAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stream = workflow.ExecuteStreamingAsync("Review", timeout.Token).GetAsyncEnumerator(timeout.Token);
        try
        {
            while (await stream.MoveNextAsync())
                if (stream.Current is TextDeltaEvent) break;
        }
        finally { await stream.DisposeAsync(); }
        await client.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static Task<HPD.Agent.Agent> CreateAgent(IChatClient client) => new AgentBuilder(new AgentConfig
    {
        Clients = new AgentClientsConfig
        {
            Chat = new ChatClientConfig
            { Override = ClientOverride<IChatClient>.Borrow(client, "test", "local", "test.adapter") }
        }
    }).WithEventComposition(CoreAgentEventComposition.Instance).BuildAsync();

    private sealed class StreamingClient(bool block) : IChatClient
    {
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Started") { MessageId = "review" };
                if (block) await Task.Delay(Timeout.Infinite, cancellationToken);
                yield return new ChatResponseUpdate(ChatRole.Assistant, " done")
                    { MessageId = "review", FinishReason = ChatFinishReason.Stop };
            }
            finally { Stopped.TrySetResult(); }
        }
    }
}
