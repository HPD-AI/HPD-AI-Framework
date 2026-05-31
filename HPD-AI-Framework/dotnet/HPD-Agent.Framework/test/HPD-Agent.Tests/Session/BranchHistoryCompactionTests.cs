using FluentAssertions;
using HPD.Agent;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Session;

public class BranchHistoryCompactionTests
{
    [Fact]
    public void Planner_ReturnsNull_ForPreserveBranchHistory()
    {
        var branch = CreateBranch(5);
        var compaction = CreateCompaction(branch, compactedCount: 3);

        var plan = new BranchCompactionPlanner().Plan(
            branch,
            compaction,
            new PreserveBranchHistoryOptions());

        plan.Should().BeNull();
    }

    [Fact]
    public void Planner_ExactBoundary_SelectsOnlyModelCompactedMessages()
    {
        var branch = CreateBranch(5);
        var compaction = CreateCompaction(branch, compactedCount: 3);

        var plan = new BranchCompactionPlanner().Plan(
            branch,
            compaction,
            new DeleteCompactedMessagesOptions());

        plan.Should().NotBeNull();
        plan!.DurableCompactedMessages.Select(m => m.MessageId)
            .Should().Equal("message-0", "message-1", "message-2");
    }

    [Fact]
    public void Planner_IncludePreviousBoundary_DoesNotRemoveRetainedMessages()
    {
        var branch = CreateBranch(5);
        var compaction = CreateCompaction(branch, compactedCount: 2);

        var plan = new BranchCompactionPlanner().Plan(
            branch,
            compaction,
            new DeleteCompactedMessagesOptions
            {
                Boundary = new IncludePreviousMessagesBoundaryOptions(2)
            });

        plan.Should().NotBeNull();
        plan!.DurableCompactedMessages.Select(m => m.MessageId)
            .Should().Equal("message-0", "message-1");
    }

    [Fact]
    public void Planner_MessageTurnBoundary_ExpandsToMessagesWithSameTurnMetadata()
    {
        var branch = CreateBranch(4);
        branch.Messages[0].AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [BranchHistoryCompactionMetadata.MessageTurnIdPropertyName] = "turn-a"
        };
        branch.Messages[1].AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [BranchHistoryCompactionMetadata.MessageTurnIdPropertyName] = "turn-a"
        };
        branch.Messages[2].AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [BranchHistoryCompactionMetadata.MessageTurnIdPropertyName] = "turn-b"
        };

        var compaction = CompactionResult.FromOriginalAndCompacted(
            branch.Messages,
            branch.Messages.Skip(2).ToList(),
            new MessageCountingCompactionOptions());

        var plan = new BranchCompactionPlanner().Plan(
            branch,
            compaction,
            new DeleteCompactedMessagesOptions { Boundary = new IncludeMessageTurnBoundaryOptions() });

        plan!.DurableCompactedMessages.Select(m => m.MessageId)
            .Should().Equal("message-0", "message-1");
    }

    [Fact]
    public void Planner_ToolCallGroupBoundary_ExpandsToMatchingCallResult()
    {
        var branch = new Branch("session", "main");
        branch.AddMessage(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "Lookup")])
        {
            MessageId = "assistant-call"
        });
        branch.AddMessage(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "done")])
        {
            MessageId = "tool-result"
        });
        branch.AddMessage(new ChatMessage(ChatRole.User, "next")
        {
            MessageId = "next-user"
        });

        var compaction = CompactionResult.FromOriginalAndCompacted(
            branch.Messages,
            branch.Messages.Skip(2).ToList(),
            new MessageCountingCompactionOptions());

        var plan = new BranchCompactionPlanner().Plan(
            branch,
            compaction,
            new DeleteCompactedMessagesOptions { Boundary = new IncludeToolCallGroupBoundaryOptions() });

        plan!.DurableCompactedMessages.Select(m => m.MessageId)
            .Should().Equal("assistant-call", "tool-result");
    }

    [Fact]
    public async Task Compactor_DeleteRetention_RemovesDurableMessagesFromLiveBranch()
    {
        var branch = CreateBranch(5);
        var compaction = CreateCompaction(branch, compactedCount: 3);
        var plan = new BranchCompactionPlanner().Plan(
            branch,
            compaction,
            new DeleteCompactedMessagesOptions())!;

        var result = await new BranchHistoryCompactor().CompactAsync(branch, plan, CancellationToken.None);

        result.DurableCompactedMessageIds.Should().Equal("message-0", "message-1", "message-2");
        result.ReplacementMessageIds.Should().BeEmpty();
        branch.Messages.Select(m => m.MessageId).Should().Equal("message-3", "message-4");
    }

    [Fact]
    public async Task Compactor_CompactRetention_InsertsReplacementMessages()
    {
        var branch = CreateBranch(5);
        var compaction = CreateCompaction(branch, compactedCount: 3, includeSummary: true);
        var plan = new BranchCompactionPlanner().Plan(
            branch,
            compaction,
            new CompactBranchHistoryOptions())!;

        var result = await new BranchHistoryCompactor().CompactAsync(branch, plan, CancellationToken.None);

        result.DurableCompactedMessageIds.Should().Equal("message-0", "message-1", "message-2");
        result.ReplacementMessageIds.Should().ContainSingle("summary");
        branch.Messages.Select(m => m.MessageId).Should().Equal("summary", "message-3", "message-4");
    }

    [Fact]
    public void Projector_AppliesBranchHistoryCompactedEvent()
    {
        var events = new List<AgentEvent>
        {
            BranchEventFactory.BranchCreated(new Branch("session", "main")),
        };

        var branch = CreateBranch(4);
        foreach (var message in branch.Messages)
        {
            events.Add(BranchEventFactory.MessageStarted("session", "main", message));
            events.Add(BranchEventFactory.ContentAdded("session", "main", message.MessageId!, message.Contents[0]));
            events.Add(BranchEventFactory.MessageCompleted("session", "main", message.MessageId!));
        }

        events.Add(BranchEventFactory.BranchHistoryCompacted(
            "session",
            "main",
            new BranchHistoryCompactedEvent(
                "compaction",
                ["message-0", "message-1"],
                ["message-0", "message-1"],
                [new ChatMessage(ChatRole.Assistant, "summary") { MessageId = "summary" }],
                nameof(SummarizingCompactionOptions),
                nameof(CompactBranchHistoryOptions),
                nameof(ExactCompactedMessagesBoundaryOptions),
                "summary",
                DateTimeOffset.UtcNow)));

        var projected = BranchProjector.Project("session", "main", events);

        projected.Messages.Select(m => m.MessageId)
            .Should().Equal("summary", "message-2", "message-3");
        projected.Messages[0].Text.Should().Be("summary");
    }

    private static Branch CreateBranch(int messageCount)
    {
        var branch = new Branch("session", "main");
        for (var i = 0; i < messageCount; i++)
        {
            branch.AddMessage(new ChatMessage(ChatRole.User, $"Message {i}")
            {
                MessageId = $"message-{i}",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        return branch;
    }

    private static CompactionResult CreateCompaction(
        Branch branch,
        int compactedCount,
        bool includeSummary = false)
    {
        var retained = branch.Messages.Skip(compactedCount).ToList();
        var visible = includeSummary
            ? new[] { new ChatMessage(ChatRole.Assistant, "summary") { MessageId = "summary" } }.Concat(retained).ToList()
            : retained;

        return CompactionResult.FromOriginalAndCompacted(
            branch.Messages,
            visible,
            includeSummary ? new SummarizingCompactionOptions() : new MessageCountingCompactionOptions());
    }
}
