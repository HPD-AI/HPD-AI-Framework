using System.Runtime.CompilerServices;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;
using Microsoft.Extensions.AI;

namespace HPD.MultiAgent.Tests;

public sealed class WorkflowSettingsExecutionTests
{
    [Fact]
    public async Task PerLayerStreaming_EmitsNodeEventsWithLayerCompletion()
    {
        var events = await RunAsync(StreamingMode.PerLayer, enableMetrics: false);
        var layerCompleted = events
            .Select((evt, index) => (evt, index))
            .Single(item => item.evt is WorkflowLayerCompletedEvent)
            .index;
        var nodeCompleted = events
            .Select((evt, index) => (evt, index))
            .Where(item => item.evt is WorkflowAgentCompletedEvent)
            .Select(item => item.index)
            .ToArray();

        nodeCompleted.Should().HaveCount(2);
        nodeCompleted.Should().OnlyContain(index => index > layerCompleted);
    }

    [Fact]
    public async Task PerNodeStreaming_EmitsNodeEventsBeforeLayerCompletion()
    {
        var events = await RunAsync(StreamingMode.PerNode, enableMetrics: false);
        var layerCompleted = events
            .Select((evt, index) => (evt, index))
            .Single(item => item.evt is WorkflowLayerCompletedEvent)
            .index;
        var nodeCompleted = events
            .Select((evt, index) => (evt, index))
            .Where(item => item.evt is WorkflowAgentCompletedEvent)
            .Select(item => item.index)
            .ToArray();

        nodeCompleted.Should().HaveCount(2);
        nodeCompleted.Should().OnlyContain(index => index < layerCompleted);
    }

    [Fact]
    public async Task EnableMetrics_CollectsCompletedWorkflowMetrics()
    {
        var config = CreateConfig(StreamingMode.PerNode, enableMetrics: true);
        var workflow = await new MultiAgentFactory().BuildAsync(config);

        await foreach (var _ in workflow.ExecuteStreamingAsync("Review"))
        {
        }

        workflow.Metrics.Should().NotBeNull();
        workflow.Metrics!.CompletedWorkflows.Should().ContainSingle();
    }

    private static async Task<List<HPD.Events.Event>> RunAsync(
        StreamingMode streamingMode,
        bool enableMetrics)
    {
        var config = CreateConfig(streamingMode, enableMetrics);
        var workflow = await new MultiAgentFactory().BuildAsync(config);
        var events = new List<HPD.Events.Event>();
        await foreach (var evt in workflow.ExecuteStreamingAsync("Review"))
            events.Add(evt);
        return events;
    }

    private static MultiAgentWorkflowConfig CreateConfig(
        StreamingMode streamingMode,
        bool enableMetrics) =>
        new()
        {
            Name = "Parallel review",
            Agents = new Dictionary<string, AgentNodeConfig>
            {
                ["one"] = new() { Agent = AgentConfig("one") },
                ["two"] = new() { Agent = AgentConfig("two") }
            },
            Edges =
            [
                new() { From = "START", To = "one" },
                new() { From = "START", To = "two" },
                new() { From = "one", To = "END" },
                new() { From = "two", To = "END" }
            ],
            Settings = new WorkflowSettingsConfig
            {
                StreamingMode = streamingMode,
                EnableMetrics = enableMetrics
            }
        };

    private static AgentConfig AgentConfig(string name) =>
        new()
        {
            Name = name,
            EventComposition = CoreAgentEventComposition.Instance,
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    Override = ClientOverride<IChatClient>.Borrow(
                        new StreamingClient(name),
                        "test",
                        "local",
                        "test.adapter")
                }
            }
        };

    private sealed class StreamingClient(string name) : IChatClient
    {
        public void Dispose()
        {
        }

        public object? GetService(
            Type serviceType,
            object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate>
            GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                $"{name} complete")
            {
                MessageId = name,
                FinishReason = ChatFinishReason.Stop
            };
            await Task.CompletedTask;
        }
    }
}
