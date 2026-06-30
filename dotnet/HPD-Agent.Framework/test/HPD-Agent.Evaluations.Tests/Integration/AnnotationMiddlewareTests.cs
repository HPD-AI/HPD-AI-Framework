// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Evaluations.Annotation;
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Providers;

namespace HPD.Agent.Evaluations.Tests.Integration;

public sealed class AnnotationMiddlewareTests
{
    [Fact]
    public async Task AnnotationQueue_ScoreBelowThreshold_EmitsAnnotationRequestAndQueuesItem()
    {
        var client = new StubChatClient();
        client.EnqueueText("Done.");
        var queue = new AnnotationQueue(new AnnotationQueueOptions
        {
            AutoQueueBelowScore = 0.5,
            LockTimeout = TimeSpan.FromSeconds(5),
        });

        var builder = new AgentBuilder(MakeConfig(), new StubProviderRegistry(client));
        builder
            .AddEvaluator(new StubDeterministicEvaluator("Flagged", pass: false), policy: EvalPolicy.TrackTrend)
            .AddAnnotationQueue(queue);

        var agent = await builder.BuildAsync(CancellationToken.None);

        await agent.RunAsync("Hello", runConfig: new AgentRunConfig());

        var item = await WaitForAsync(
            () => queue.GetPending().SingleOrDefault(),
            value => value is not null,
            "annotation request to be queued");

        item.TriggerEvaluatorName.Should().Be("StubDeterministicEvaluator");
        item.TriggerScore.Should().Be(0.0);
    }

    private static AgentConfig MakeConfig(string name = "AnnotationTestAgent") => new()
    {
        Name = name,
        SystemInstructions = "You are a test agent.",
        MaxAgenticIterations = 3,
        Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "test", ModelName = "test-model" } },
        AgenticLoop = new AgenticLoopConfig { MaxTurnDuration = TimeSpan.FromSeconds(10) },
    };

    private static async Task<T> WaitForAsync<T>(
        Func<T> read,
        Func<T, bool> isReady,
        string description)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var value = read();
            if (isReady(value))
            {
                return value;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }

}
