// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using FluentAssertions;
using Microsoft.Extensions.AI;
using HPD.Agent.Evaluations.Evaluators.LlmJudge;
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Evaluations.Tests.Infrastructure;

namespace HPD.Agent.Evaluations.Tests.Integration;

public sealed class LiveEvaluationMiddlewareConversationStateTests
{
    [Fact]
    public void AdvanceConversationEvalState_FirstTurn_SetsGoalAndStoresResponse()
    {
        var turnCtx = new TestContextBuilder()
            .WithUserInput("Help me plan the rollout")
            .WithOutputText("First, define the rollout stages.")
            .Build();

        var next = LiveEvaluationMiddleware.AdvanceConversationEvalState(
            new ConversationEvalStateData(),
            turnCtx);

        next.EstablishedGoal.Should().Be("Help me plan the rollout");
        next.TurnCount.Should().Be(1);
        next.PriorResponses.Should().ContainSingle()
            .Which.Should().Be("First, define the rollout stages.");
    }

    [Fact]
    public void AdvanceConversationEvalState_ExistingGoal_PreservesGoalAndAppendsResponse()
    {
        var state = new ConversationEvalStateData
        {
            EstablishedGoal = "Existing goal",
            PriorResponses = ["Earlier response"],
            TurnCount = 2,
        };
        var turnCtx = new TestContextBuilder()
            .WithUserInput("Continue")
            .WithOutputText("New response")
            .Build();

        var next = LiveEvaluationMiddleware.AdvanceConversationEvalState(state, turnCtx);

        next.EstablishedGoal.Should().Be("Existing goal");
        next.TurnCount.Should().Be(3);
        next.PriorResponses.Should().Equal("Earlier response", "New response");
    }

    [Fact]
    public void AdvanceConversationEvalState_LongResponse_TruncatesStoredResponse()
    {
        string longResponse = new('x', 4500);
        var turnCtx = new TestContextBuilder()
            .WithOutputText(longResponse)
            .Build();

        var next = LiveEvaluationMiddleware.AdvanceConversationEvalState(
            new ConversationEvalStateData(),
            turnCtx);

        next.PriorResponses.Should().ContainSingle()
            .Which.Length.Should().Be(4000);
    }

    [Fact]
    public void BuildConversationHistoryForEvaluation_MergesThreadHistoryAndStoredResponses()
    {
        var threadAssistant = new ChatMessage(ChatRole.Assistant, "Thread history response");
        var threadUser = new ChatMessage(ChatRole.User, "Earlier user message");
        var turnCtx = new TestContextBuilder()
            .WithConversationHistory(threadUser, threadAssistant)
            .Build();
        var state = new ConversationEvalStateData
        {
            PriorResponses = ["Stored response"],
        };

        var history = LiveEvaluationMiddleware.BuildConversationHistoryForEvaluation(turnCtx, state);

        history.Should().HaveCount(3);
        history[0].Should().BeSameAs(threadUser);
        history[1].Should().BeSameAs(threadAssistant);
        history[2].Role.Should().Be(ChatRole.Assistant);
        history[2].Text.Should().Be("Stored response");
    }

    [Fact]
    public void BuildConversationHistoryForEvaluation_DoesNotDuplicateStoredResponsesAlreadyInThreadHistory()
    {
        var existing = new ChatMessage(ChatRole.Assistant, "Already present");
        var turnCtx = new TestContextBuilder()
            .WithConversationHistory(existing)
            .Build();
        var state = new ConversationEvalStateData
        {
            PriorResponses = ["Already present"],
        };

        var history = LiveEvaluationMiddleware.BuildConversationHistoryForEvaluation(turnCtx, state);

        history.Should().ContainSingle();
        history[0].Should().BeSameAs(existing);
    }
}
