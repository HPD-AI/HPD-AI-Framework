// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI;
using HPD.Agent;
using HPD.Agent.Evaluations.Integration;

namespace HPD.Agent.Evaluations.Tests.Integration;

/// <summary>
/// Tests for TurnEvaluationContextBuilder.FromBranch — the retroactive path.
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
/// 9. Empty branch → zero contexts returned.
/// 10. AgentName propagated to context.
/// </summary>
public sealed class TurnEvaluationContextBuilderTests
{
    // Use internal access via InternalsVisibleTo
    private static IReadOnlyList<TurnEvaluationContext> FromBranch(Branch branch, string agentName = "TestAgent")
        => TurnEvaluationContextBuilder.FromBranch(branch, agentName);

    // ── Basic single turn ─────────────────────────────────────────────────────

    [Fact]
    public void FromBranch_SingleTurn_OneContext()
    {
        var branch = new BranchBuilder()
            .AddUserMessage("What is 2+2?")
            .AddAssistantMessage("4")
            .Build();

        var contexts = FromBranch(branch);

        contexts.Should().ContainSingle();
    }

    [Fact]
    public void FromBranch_SingleTurn_UserInputAndOutputText()
    {
        var branch = new BranchBuilder()
            .AddUserMessage("What is 2+2?")
            .AddAssistantMessage("The answer is 4.")
            .Build();

        var ctx = FromBranch(branch).Single();

        ctx.UserInput.Should().Be("What is 2+2?");
        ctx.OutputText.Should().Be("The answer is 4.");
    }

    [Fact]
    public void FromBranch_SingleTurn_TurnIndexIsZero()
    {
        var branch = new BranchBuilder()
            .AddUserMessage("Hi")
            .AddAssistantMessage("Hello")
            .Build();

        FromBranch(branch).Single().TurnIndex.Should().Be(0);
    }

    [Fact]
    public void FromBranch_AgentNamePropagated()
    {
        var branch = new BranchBuilder().AddUserMessage("Hi").AddAssistantMessage("Hello").Build();

        FromBranch(branch, agentName: "MyAgent").Single().AgentName.Should().Be("MyAgent");
    }

    [Fact]
    public void FromBranch_SessionIdAndBranchIdPropagated()
    {
        var branch = new BranchBuilder("session-xyz", "branch-abc")
            .AddUserMessage("Hi")
            .AddAssistantMessage("Hello")
            .Build();

        var ctx = FromBranch(branch).Single();
        ctx.SessionId.Should().Be("session-xyz");
        ctx.BranchId.Should().Be("branch-abc");
    }

    // ── Multi-turn ────────────────────────────────────────────────────────────

    [Fact]
    public void FromBranch_TwoTurns_TwoContexts()
    {
        var branch = new BranchBuilder()
            .AddUserMessage("Turn 1")
            .AddAssistantMessage("Response 1")
            .AddUserMessage("Turn 2")
            .AddAssistantMessage("Response 2")
            .Build();

        FromBranch(branch).Should().HaveCount(2);
    }

    [Fact]
    public void FromBranch_TwoTurns_TurnIndicesAreSequential()
    {
        var branch = new BranchBuilder()
            .AddUserMessage("Turn 1")
            .AddAssistantMessage("Response 1")
            .AddUserMessage("Turn 2")
            .AddAssistantMessage("Response 2")
            .Build();

        var contexts = FromBranch(branch);
        contexts[0].TurnIndex.Should().Be(0);
        contexts[1].TurnIndex.Should().Be(1);
    }

    [Fact]
    public void FromBranch_SecondTurn_ConversationHistoryContainsPreviousMessages()
    {
        var branch = new BranchBuilder()
            .AddUserMessage("First question")
            .AddAssistantMessage("First answer")
            .AddUserMessage("Second question")
            .AddAssistantMessage("Second answer")
            .Build();

        var ctx1 = FromBranch(branch)[1]; // second turn

        // History should contain the first user + assistant messages
        ctx1.ConversationHistory.Should().HaveCount(2,
            "second turn's history contains first user + assistant messages");
        ctx1.ConversationHistory[0].Role.Should().Be(ChatRole.User);
        ctx1.ConversationHistory[1].Role.Should().Be(ChatRole.Assistant);
    }

    [Fact]
    public void FromBranch_FirstTurn_ConversationHistoryIsEmpty()
    {
        var branch = new BranchBuilder()
            .AddUserMessage("First question")
            .AddAssistantMessage("First answer")
            .Build();

        FromBranch(branch)[0].ConversationHistory.Should().BeEmpty(
            "first turn has no prior messages");
    }

    // ── Incomplete turn ───────────────────────────────────────────────────────

    [Fact]
    public void FromBranch_IncompleteTurn_Skipped()
    {
        // User message with no assistant reply — use internal Branch constructor
        var b = new Branch("s1", "b1");
        b.Messages.Add(new ChatMessage(ChatRole.User, "Unanswered"));

        FromBranch(b).Should().BeEmpty("incomplete turns must be skipped");
    }

    // ── Empty branch ──────────────────────────────────────────────────────────

    [Fact]
    public void FromBranch_EmptyBranch_ReturnsEmpty()
    {
        var branch = new Branch("s1", "b1"); // Messages is empty by default

        FromBranch(branch).Should().BeEmpty();
    }

    // ── Tool calls ────────────────────────────────────────────────────────────

    [Fact]
    public void FromBranch_WithToolCall_ToolCallRecordPresent()
    {
        var branch = new BranchBuilder()
            .AddUserMessage("Search for cats")
            .AddToolCall("call-1", "SearchTool", "cats found")
            .AddAssistantMessage("I found cats.")
            .Build();

        var ctx = FromBranch(branch).Single();

        ctx.ToolCalls.Should().ContainSingle(tc => tc.Name == "SearchTool",
            "tool call must be captured as a ToolCallRecord");
    }

    [Fact]
    public void FromBranch_WithToolCall_ResultPropagated()
    {
        var branch = new BranchBuilder()
            .AddUserMessage("Fetch data")
            .AddToolCall("call-1", "FetchTool", "the result")
            .AddAssistantMessage("Done.")
            .Build();

        var ctx = FromBranch(branch).Single();
        ctx.ToolCalls.Single(tc => tc.Name == "FetchTool").Result.Should().Be("the result");
    }

    [Fact]
    public void FromBranch_NoToolCalls_EmptyToolCallList()
    {
        var branch = new BranchBuilder()
            .AddUserMessage("Simple question")
            .AddAssistantMessage("Simple answer")
            .Build();

        FromBranch(branch).Single().ToolCalls.Should().BeEmpty();
    }

    // ── StopKind inference ────────────────────────────────────────────────────

    [Fact]
    public void FromBranch_AssistantEndsWithQuestion_StopKindIsAskedClarification()
    {
        var branch = new BranchBuilder()
            .AddUserMessage("Do the thing")
            .AddAssistantMessage("Could you clarify what you mean?")
            .Build();

        FromBranch(branch).Single().StopKind.Should().Be(AgentStopKind.AskedClarification);
    }

    [Fact]
    public void FromBranch_AssistantNormalText_StopKindIsUnknown()
    {
        // No finish reason on retroactive path → Unknown (unless text ends with ?)
        var branch = new BranchBuilder()
            .AddUserMessage("Tell me something")
            .AddAssistantMessage("The sky is blue.")
            .Build();

        // No finish reason in ChatResponse built from a ChatMessage → Unknown
        FromBranch(branch).Single().StopKind.Should().Be(AgentStopKind.Unknown);
    }

    // ── Usage / trace defaults for retroactive path ───────────────────────────

    [Fact]
    public void FromBranch_RetroactivePath_UsageIsNull()
    {
        var branch = new BranchBuilder()
            .AddUserMessage("Hi")
            .AddAssistantMessage("Hello")
            .Build();

        var ctx = FromBranch(branch).Single();
        ctx.TurnUsage.Should().BeNull("token usage is not persisted to branch");
        ctx.IterationCount.Should().Be(0);
        ctx.Duration.Should().Be(TimeSpan.Zero);
    }
}
