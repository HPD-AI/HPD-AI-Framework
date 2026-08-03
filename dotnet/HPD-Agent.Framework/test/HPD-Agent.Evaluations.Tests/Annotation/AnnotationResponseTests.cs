// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Evaluations;
using HPD.Agent.Evaluations.Annotation;
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Evaluations.Storage;
using HPD.Agent.Evaluations.Tests.Integration;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Tests.Annotation;

public sealed class AnnotationResponseTests
{
    [Fact]
    public async Task SendAnnotationResponse_CompletesAgentCoordinatorWait()
    {
        var agent = await BuildAgentAsync();
        var wait = agent.EventCoordinator.RequestAsync<AnnotationRequestedEvent, AnnotationResponseEvent>(
            new AnnotationRequestedEvent
            {
                AnnotationId = "annotation-123",
                SessionId = "session-1",
                ThreadId = "thread-1",
                TurnIndex = 1,
                TriggerEvaluatorName = "test",
                TriggerScore = 0.5,
            },
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        await agent.SendAnnotationResponseAsync(
            "annotation-123",
            reviewerId: "reviewer-1",
            label: "pass",
            score: 1.0,
            comment: "Looks good.");

        var response = await wait;

        response.AnnotationId.Should().Be("annotation-123");
        response.ReviewerId.Should().Be("reviewer-1");
        response.Label.Should().Be("pass");
        response.Score.Should().Be(1.0);
        response.Comment.Should().Be("Looks good.");
    }

    [Fact]
    public async Task HumanAnnotationResult_CanBeStoredAsHumanSource()
    {
        var store = new InMemoryScoreStore();
        var response = new AnnotationResponseEvent
        {
            AnnotationId = "annotation-123",
            ReviewerId = "reviewer-1",
            Label = "pass",
            Score = 1.0,
            Comment = "Human verified.",
        };
        var result = LiveEvaluationMiddleware.BuildHumanAnnotationResult(
            response.AnnotationId,
            "Task Success",
            response);

        await store.WriteScoreAsync(new ScoreRecord
        {
            Id = Guid.NewGuid().ToString(),
            EvaluatorName = "Task Success",
            EvaluatorVersion = "human",
            Result = result,
            Source = EvaluationSource.Human,
            SessionId = "session",
            ThreadId = "thread",
            TurnIndex = 0,
            AgentName = "agent",
            SamplingRate = 1.0,
            Policy = EvalPolicy.TrackTrend,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var records = new List<ScoreRecord>();
        await foreach (var record in store.GetScoresAsync(
                           "Task Success",
                           from: null,
                           to: null,
                           ct: CancellationToken.None))
        {
            records.Add(record);
        }

        records.Should().ContainSingle()
            .Which.Source.Should().Be(EvaluationSource.Human);
        records.Single().Result.Metrics["Task Success"]
            .Should().BeOfType<NumericMetric>()
            .Which.Value.Should().Be(1.0);
    }

    [Fact]
    public void HumanAnnotationResult_UsesBooleanMetricWhenLabelIsBoolean()
    {
        var result = LiveEvaluationMiddleware.BuildHumanAnnotationResult(
            "annotation-123",
            "Approved",
            new AnnotationResponseEvent
            {
                AnnotationId = "annotation-123",
                ReviewerId = "reviewer-1",
                Label = "true",
            });

        result.Metrics["Approved"].Should().BeOfType<BooleanMetric>()
            .Which.Value.Should().BeTrue();
    }

    private static async Task<Agent> BuildAgentAsync()
    {
        var config = new AgentConfig
        {
            Name = "AnnotationResponseTestAgent",
            SystemInstructions = "You are a test agent.",
            Clients = new AgentClientsConfig { Chat = new ProviderClientConfig { ProviderKey = "test", ModelName = "test-model" } },
        };

        return await new AgentBuilder(config, new StubProviderRegistry(new StubChatClient()))
            .BuildAsync(CancellationToken.None);
    }
}
