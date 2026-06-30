// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using FluentAssertions;
using Microsoft.Extensions.AI;
using HPD.Agent;
using HPD.Agent.Evaluations.Integration;

namespace HPD.Agent.Evaluations.Tests.Integration;

/// <summary>
/// Tests for TurnEvaluationContextBuilder.FromThread — the retroactive path.
///
/// Key behaviors:
/// 1. Each user→assistant exchange becomes one TurnEvaluationContext.
/// 2. Incomplete turns (user with no assistant reply) are skipped.
/// 3. TurnIndex increments per user message.
/// 4. UserInput is the user message text.
/// 5. OutputText is the assistant message text.
/// 6. ConversationHistory contains all messages BEFORE the current user message.
/// 7. Tool calls between user and assistant are captured as ToolCallRecords.
/// 8. InferStopKind: text ending in '?' → AskedClarification, "stop" finish → Completed.
/// 9. Empty thread → zero contexts returned.
/// 10. AgentName propagated to context.
/// </summary>
public sealed class TurnEvaluationContextBuilderTests
{
    // Use internal access via InternalsVisibleTo
    private static IReadOnlyList<TurnEvaluationContext> FromThread(Thread thread, string agentName = "TestAgent")
        => TurnEvaluationContextBuilder.FromThread(thread, agentName);

    // ── Basic single turn ─────────────────────────────────────────────────────

    [Fact]
    public void FromThread_SingleTurn_OneContext()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("What is 2+2?")
            .AddAssistantMessage("4")
            .Build();

        var contexts = FromThread(thread);

        contexts.Should().ContainSingle();
    }

    [Fact]
    public void FromThread_SingleTurn_UserInputAndOutputText()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("What is 2+2?")
            .AddAssistantMessage("The answer is 4.")
            .Build();

        var ctx = FromThread(thread).Single();

        ctx.UserInput.Should().Be("What is 2+2?");
        ctx.OutputText.Should().Be("The answer is 4.");
    }

    [Fact]
    public void FromThread_SingleTurn_TurnIndexIsZero()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("Hi")
            .AddAssistantMessage("Hello")
            .Build();

        FromThread(thread).Single().TurnIndex.Should().Be(0);
    }

    [Fact]
    public void FromThread_AgentNamePropagated()
    {
        var thread = new ThreadBuilder().AddUserMessage("Hi").AddAssistantMessage("Hello").Build();

        FromThread(thread, agentName: "MyAgent").Single().AgentName.Should().Be("MyAgent");
    }

    [Fact]
    public void FromThread_SessionIdAndThreadIdPropagated()
    {
        var thread = new ThreadBuilder("session-xyz", "thread-abc")
            .AddUserMessage("Hi")
            .AddAssistantMessage("Hello")
            .Build();

        var ctx = FromThread(thread).Single();
        ctx.SessionId.Should().Be("session-xyz");
        ctx.ThreadId.Should().Be("thread-abc");
    }

    // ── Multi-turn ────────────────────────────────────────────────────────────

    [Fact]
    public void FromThread_TwoTurns_TwoContexts()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("Turn 1")
            .AddAssistantMessage("Response 1")
            .AddUserMessage("Turn 2")
            .AddAssistantMessage("Response 2")
            .Build();

        FromThread(thread).Should().HaveCount(2);
    }

    [Fact]
    public void FromThread_TwoTurns_TurnIndicesAreSequential()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("Turn 1")
            .AddAssistantMessage("Response 1")
            .AddUserMessage("Turn 2")
            .AddAssistantMessage("Response 2")
            .Build();

        var contexts = FromThread(thread);
        contexts[0].TurnIndex.Should().Be(0);
        contexts[1].TurnIndex.Should().Be(1);
    }

    [Fact]
    public void FromThread_SecondTurn_ConversationHistoryContainsPreviousMessages()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("First question")
            .AddAssistantMessage("First answer")
            .AddUserMessage("Second question")
            .AddAssistantMessage("Second answer")
            .Build();

        var ctx1 = FromThread(thread)[1]; // second turn

        // History should contain the first user + assistant messages
        ctx1.ConversationHistory.Should().HaveCount(2,
            "second turn's history contains first user + assistant messages");
        ctx1.ConversationHistory[0].Role.Should().Be(ChatRole.User);
        ctx1.ConversationHistory[1].Role.Should().Be(ChatRole.Assistant);
    }

    [Fact]
    public void FromThread_FirstTurn_ConversationHistoryIsEmpty()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("First question")
            .AddAssistantMessage("First answer")
            .Build();

        FromThread(thread)[0].ConversationHistory.Should().BeEmpty(
            "first turn has no prior messages");
    }

    // ── Incomplete turn ───────────────────────────────────────────────────────

    [Fact]
    public void FromThread_IncompleteTurn_Skipped()
    {
        // User message with no assistant reply — use internal Thread constructor
        var b = new Thread("s1", "b1");
        b.Messages.Add(new ChatMessage(ChatRole.User, "Unanswered"));

        FromThread(b).Should().BeEmpty("incomplete turns must be skipped");
    }

    // ── Empty thread ──────────────────────────────────────────────────────────

    [Fact]
    public void FromThread_EmptyThread_ReturnsEmpty()
    {
        var thread = new Thread("s1", "b1"); // Messages is empty by default

        FromThread(thread).Should().BeEmpty();
    }

    // ── Tool calls ────────────────────────────────────────────────────────────

    [Fact]
    public void FromThread_WithToolCall_ToolCallRecordPresent()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("Search for cats")
            .AddToolCall("call-1", "SearchTool", "cats found")
            .AddAssistantMessage("I found cats.")
            .Build();

        var ctx = FromThread(thread).Single();

        ctx.ToolCalls.Should().ContainSingle(tc => tc.Name == "SearchTool",
            "tool call must be captured as a ToolCallRecord");
    }

    [Fact]
    public void FromThread_WithToolCall_ResultPropagated()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("Fetch data")
            .AddToolCall("call-1", "FetchTool", "the result")
            .AddAssistantMessage("Done.")
            .Build();

        var ctx = FromThread(thread).Single();
        ctx.ToolCalls.Single(tc => tc.Name == "FetchTool").Result.Should().Be("the result");
    }

    [Fact]
    public void FromThread_NoToolCalls_EmptyToolCallList()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("Simple question")
            .AddAssistantMessage("Simple answer")
            .Build();

        FromThread(thread).Single().ToolCalls.Should().BeEmpty();
    }

    // ── StopKind inference ────────────────────────────────────────────────────

    [Fact]
    public void FromThread_AssistantEndsWithQuestion_StopKindIsAskedClarification()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("Do the thing")
            .AddAssistantMessage("Could you clarify what you mean?")
            .Build();

        FromThread(thread).Single().StopKind.Should().Be(AgentStopKind.AskedClarification);
    }

    [Fact]
    public void FromThread_AssistantRequestsCredentials_StopKindIsRequestedCredentials()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("Deploy the app")
            .AddAssistantMessage("I need an API key before I can continue.")
            .Build();

        FromThread(thread).Single().StopKind.Should().Be(AgentStopKind.RequestedCredentials);
    }

    [Fact]
    public void FromThread_AssistantRequestsApproval_StopKindIsAwaitingConfirmation()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("Delete the staging database")
            .AddAssistantMessage("Please confirm before I proceed.")
            .Build();

        FromThread(thread).Single().StopKind.Should().Be(AgentStopKind.AwaitingConfirmation);
    }

    [Fact]
    public void FromThread_AssistantNormalText_StopKindIsUnknown()
    {
        // No finish reason on retroactive path → Unknown (unless text ends with ?)
        var thread = new ThreadBuilder()
            .AddUserMessage("Tell me something")
            .AddAssistantMessage("The sky is blue.")
            .Build();

        // No finish reason in ChatResponse built from a ChatMessage → Unknown
        FromThread(thread).Single().StopKind.Should().Be(AgentStopKind.Unknown);
    }

    // ── Usage / trace defaults for retroactive path ───────────────────────────

    [Fact]
    public void FromThread_RetroactivePath_UsageIsNull()
    {
        var thread = new ThreadBuilder()
            .AddUserMessage("Hi")
            .AddAssistantMessage("Hello")
            .Build();

        var ctx = FromThread(thread).Single();
        ctx.TurnUsage.Should().BeNull("token usage is not persisted to thread");
        ctx.IterationCount.Should().Be(0);
        ctx.Duration.Should().Be(TimeSpan.Zero);
    }
}
