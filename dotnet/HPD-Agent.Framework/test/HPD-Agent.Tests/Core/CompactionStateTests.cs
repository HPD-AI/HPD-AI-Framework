using System.Collections.Immutable;
using FluentAssertions;
using HPD.Agent;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Core;

public class CompactionStateTests
{
    [Fact]
    public void CompactionResult_FromOriginalAndCompacted_IdentifiesRetainedAndCompactedMessages()
    {
        var original = CreateMessages(5);
        var compacted = original.Skip(2).ToList();

        var result = CompactionResult.FromOriginalAndCompacted(
            original,
            compacted,
            new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 3 });

        result.OriginalMessages.Should().HaveCount(5);
        result.ModelVisibleMessages.Should().Equal(compacted);
        result.RetainedMessages.Should().Equal(compacted);
        result.ModelCompactedMessages.Should().Equal(original.Take(2));
        result.ReplacementMessages.Should().BeEmpty();
        result.SummaryContent.Should().BeNull();
    }

    [Fact]
    public void CompactionResult_FromOriginalAndCompacted_IdentifiesReplacementMessages()
    {
        var original = CreateMessages(5);
        var summary = new ChatMessage(ChatRole.Assistant, "Summary of older context");
        var compacted = new[] { summary }.Concat(original.Skip(3)).ToList();

        var result = CompactionResult.FromOriginalAndCompacted(
            original,
            compacted,
            new SummarizingCompactionOptions { PreserveRecentUserTurnCount = 2 });

        result.ReplacementMessages.Should().ContainSingle().Which.Should().BeSameAs(summary);
        result.RetainedMessages.Should().Equal(original.Skip(3));
        result.ModelCompactedMessages.Should().Equal(original.Take(3));
        result.SummaryContent.Should().Be("Summary of older context");
        summary.MessageId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ResolvePreserveRecentRawMessageCount_CountsRecentUserTurns()
    {
        var messages = new List<ChatMessage>
        {
            TurnMessage(ChatRole.User, "old-user", "turn-old"),
            TurnMessage(ChatRole.Assistant, "old-assistant", "turn-old"),
            TurnMessage(ChatRole.User, "new-user", "turn-new"),
            TurnMessage(ChatRole.Assistant, "new-assistant", "turn-new"),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "done")])
            {
                MessageId = "new-tool",
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    [ThreadHistoryCompactionMetadata.MessageTurnIdPropertyName] = "turn-new"
                }
            }
        };

        var preserveRawMessageCount = AgentBuilder.ResolvePreserveRecentRawMessageCount(
            messages,
            preserveRecentUserTurnCount: 1);

        preserveRawMessageCount.Should().Be(3);
    }

    [Fact]
    public void ResolvePreserveRecentRawMessageCount_ZeroPreservesNoRawMessages()
    {
        var messages = CreateMessages(4);

        var preserveRawMessageCount = AgentBuilder.ResolvePreserveRecentRawMessageCount(
            messages,
            preserveRecentUserTurnCount: 0);

        preserveRawMessageCount.Should().Be(0);
    }

    [Fact]
    public void ResolvePreserveRecentRawMessageCount_PreservesFromSelectedMessageTurn()
    {
        var messages = new List<ChatMessage>
        {
            TurnMessage(ChatRole.User, "old-user", "turn-old"),
            TurnMessage(ChatRole.Assistant, "old-assistant", "turn-old"),
            TurnMessage(ChatRole.User, "selected-user", "turn-selected"),
            TurnMessage(ChatRole.Assistant, "selected-assistant", "turn-selected"),
            TurnMessage(ChatRole.User, "new-user", "turn-new"),
            TurnMessage(ChatRole.Assistant, "new-assistant", "turn-new")
        };

        var preserveRawMessageCount = AgentBuilder.ResolvePreserveRecentRawMessageCount(
            messages,
            new SummarizingCompactionOptions
            {
                PreserveRecentUserTurnCount = 1,
                PreserveFromMessageId = "selected-user",
                PreserveFromMessageTurnId = "turn-selected"
            });

        preserveRawMessageCount.Should().Be(4);
    }

    [Fact]
    public void ResolvePreserveRecentRawMessageCount_FallsBackToSelectedMessageWhenTurnIsMissing()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "old") { MessageId = "old" },
            new(ChatRole.Assistant, "old answer") { MessageId = "old-answer" },
            new(ChatRole.User, "selected") { MessageId = "selected" },
            new(ChatRole.Assistant, "selected answer") { MessageId = "selected-answer" }
        };

        var preserveRawMessageCount = AgentBuilder.ResolvePreserveRecentRawMessageCount(
            messages,
            new MessageCountingCompactionOptions
            {
                PreserveRecentUserTurnCount = 1,
                PreserveFromMessageId = "selected"
            });

        preserveRawMessageCount.Should().Be(2);
    }

    [Fact]
    public void CompactionSnapshot_FromResult_PreservesMessageIdentityMetadata()
    {
        var original = CreateMessages(4);
        var summary = new ChatMessage(ChatRole.Assistant, "Handoff summary");
        var compacted = new[] { summary }.Concat(original.Skip(2)).ToList();
        var result = CompactionResult.FromOriginalAndCompacted(
            original,
            compacted,
            new SummarizingCompactionOptions());

        var snapshot = CompactionSnapshot.FromResult(result);

        snapshot.OriginalMessageIds.Should().Equal(original.Select(m => m.MessageId));
        snapshot.ModelVisibleMessageIds.Should().Equal(compacted.Select(m => m.MessageId));
        snapshot.ModelCompactedMessageIds.Should().Equal(original.Take(2).Select(m => m.MessageId));
        snapshot.RetainedMessageIds.Should().Equal(original.Skip(2).Select(m => m.MessageId));
        snapshot.ReplacementMessageIds.Should().ContainSingle(summary.MessageId);
        snapshot.SummaryContent.Should().Be("Handoff summary");
        snapshot.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MementoBuilder_PreservesRecentCompactedUserMessagesAsReplacementClones()
    {
        var original = CreateMessages(6);
        var summary = new ChatMessage(ChatRole.Assistant, "Important decisions");
        var retained = original.Skip(4).ToList();
        var baseResult = CompactionResult.FromOriginalAndCompacted(
            original,
            new[] { summary }.Concat(retained).ToList(),
            new SummarizingCompactionOptions());

        var result = CompactionMementoBuilder.Apply(
            baseResult,
            new SummarizingCompactionOptions
            {
                Memory = new SummaryMemoryOptions
                {
                    PreserveRecentUserMessagesSeparately = true,
                    RecentUserMessageTokenBudget = 10_000
                }
            });

        result.ReplacementMessages.Should().HaveCount(6);
        result.ReplacementMessages[0].Role.Should().Be(ChatRole.System);
        result.ReplacementMessages.Skip(1).Take(4).Select(m => m.Role).Should().OnlyContain(role => role == ChatRole.User);
        result.ReplacementMessages.Last().Text.Should().StartWith("Conversation handoff summary for continuation:");
        result.ModelCompactedMessages.Select(m => m.MessageId).Should().Equal(original.Take(4).Select(m => m.MessageId));
        result.RetainedMessages.Select(m => m.MessageId).Should().Equal(original.Skip(4).Select(m => m.MessageId));
    }

    [Fact]
    public void MementoBuilder_FiltersPreviousGeneratedMementoMessages()
    {
        var original = CreateMessages(4);
        var staleMemento = new ChatMessage(ChatRole.Assistant, "stale")
        {
            MessageId = "stale-memento",
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [CompactionMementoBuilder.MementoPropertyName] = true
            }
        };

        var summary = new ChatMessage(ChatRole.Assistant, "Fresh summary");
        var baseResult = CompactionResult.FromOriginalAndCompacted(
            original,
            [staleMemento, summary, original[3]],
            new SummarizingCompactionOptions());

        var result = CompactionMementoBuilder.Apply(
            baseResult,
            new SummarizingCompactionOptions
            {
                Memory = new SummaryMemoryOptions
                {
                    FilterGeneratedContextWrappers = true,
                    PreserveRecentUserMessagesSeparately = false,
                    ReinjectCurrentContextAfterCompaction = false
                }
            });

        result.ModelVisibleMessages.Should().NotContain(m => m.MessageId == "stale-memento");
        result.ReplacementMessages.Should().ContainSingle();
        result.ReplacementMessages[0].Text.Should().Contain("Fresh summary");
    }

    [Fact]
    public void CompactionStateData_WithCompaction_StoresSnapshotAndAppliedTime()
    {
        var snapshot = new CompactionSnapshot
        {
            OriginalMessageIds = ["one"],
            ModelVisibleMessageIds = ["one"],
            RetainedMessageIds = ["one"]
        };

        var state = new CompactionStateData
        {
            MessageTurnCount = 9
        }.WithCompaction(snapshot);

        state.LastCompaction.Should().BeSameAs(snapshot);
        state.MessageTurnCount.Should().Be(0);
        state.LastAppliedAt.Should().NotBeNull();
    }

    [Fact]
    public void CompactionStateData_WithIncrementedMessageTurnCount_IsImmutable()
    {
        var original = new CompactionStateData();

        var incremented = original.WithIncrementedMessageTurnCount();

        original.MessageTurnCount.Should().Be(0);
        incremented.MessageTurnCount.Should().Be(1);
    }

    [Fact]
    public void CompactionStateData_WithObservedUsage_StoresTurnAndIterationUsage()
    {
        var turnUsage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20, TotalTokenCount = 120 };
        var iterationUsage = ImmutableList.Create<UsageDetails?>(turnUsage, null);

        var state = new CompactionStateData().WithObservedUsage(turnUsage, iterationUsage);

        state.LastTurnUsage.Should().BeSameAs(turnUsage);
        state.LastIterationUsage.Should().Equal(iterationUsage);
        state.LastUsageObservedAt.Should().NotBeNull();
    }

    private static List<ChatMessage> CreateMessages(int count)
    {
        var messages = new List<ChatMessage>();
        for (var i = 0; i < count; i++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"Message {i}")
            {
                MessageId = $"message-{i}",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        return messages;
    }

    private static ChatMessage TurnMessage(ChatRole role, string messageId, string turnId) =>
        new(role, messageId)
        {
            MessageId = messageId,
            CreatedAt = DateTimeOffset.UtcNow,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ThreadHistoryCompactionMetadata.MessageTurnIdPropertyName] = turnId
            }
        };
}
