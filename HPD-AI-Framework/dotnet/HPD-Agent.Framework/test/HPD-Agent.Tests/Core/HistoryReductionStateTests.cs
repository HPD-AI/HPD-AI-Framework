using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HPD.Agent;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Unit tests for HistoryReductionState class.
/// Tests cache validation, integrity checking, and message transformation.
/// </summary>
public class HistoryReductionStateTests
{
    #region Test Data Helpers

    private static List<ChatMessage> CreateTestMessages(int count)
    {
        var messages = new List<ChatMessage>();
        for (int i = 0; i < count; i++)
            messages.Add(new ChatMessage(ChatRole.User, $"Message {i}"));
        return messages;
    }

    private static CachedReduction CreateSampleReduction(
        List<ChatMessage> messages,
        int summarizedUpToIndex = 90,
        int targetCount = 20,
        int reductionThreshold = 5,
        int? countAtReduction = null,
        HistoryCountingUnit countingUnit = HistoryCountingUnit.Messages)
    {
        return CachedReduction.Create(
            messages,
            "Summary of old messages",
            summarizedUpToIndex,
            targetCount,
            reductionThreshold,
            countAtReduction: countAtReduction ?? messages.Count,
            countingUnit: countingUnit);
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void Create_WithValidParameters_ShouldCreateReduction()
    {
        // Arrange
        var messages = CreateTestMessages(100);
        var summaryContent = "Summary of messages 0-89";
        var summarizedUpToIndex = 90;
        var targetCount = 20;
        var reductionThreshold = 5;

        // Act
        var reduction = CachedReduction.Create(
            messages,
            summaryContent,
            summarizedUpToIndex,
            targetCount,
            reductionThreshold,
            countAtReduction: messages.Count,
            countingUnit: HistoryCountingUnit.Messages);

        // Assert
        reduction.Should().NotBeNull();
        reduction.SummarizedUpToIndex.Should().Be(summarizedUpToIndex);
        reduction.CountAtReduction.Should().Be(messages.Count);
        reduction.SummaryContent.Should().Be(summaryContent);
        reduction.TargetCount.Should().Be(targetCount);
        reduction.ReductionThreshold.Should().Be(reductionThreshold);
        reduction.CountingUnit.Should().Be(HistoryCountingUnit.Messages);
        reduction.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        reduction.MessageHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Create_WithEmptyMessages_ShouldStillWork()
    {
        // Arrange
        var messages = new List<ChatMessage>();

        // Act
        var reduction = CachedReduction.Create(
            messages,
            "Empty summary",
            0,
            20,
            5,
            countAtReduction: 0,
            countingUnit: HistoryCountingUnit.Messages);

        // Assert
        reduction.Should().NotBeNull();
        reduction.CountAtReduction.Should().Be(0);
        reduction.SummarizedUpToIndex.Should().Be(0);
    }

    #endregion

    #region IsValidFor Tests

    [Fact]
    public void IsValidFor_WithSameCount_ShouldReturnTrue()
    {
        // Arrange
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages);

        // Act
        var isValid = reduction.IsValidFor(100, HistoryCountingUnit.Messages);

        // Assert
        isValid.Should().BeTrue("no messages added or removed");
    }

    [Fact]
    public void IsValidFor_WithNewMessagesWithinThreshold_ShouldReturnTrue()
    {
        // Arrange
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, targetCount: 20, reductionThreshold: 5);

        // Act - 4 new messages (within threshold of 5)
        var isValid = reduction.IsValidFor(104, HistoryCountingUnit.Messages);

        // Assert
        isValid.Should().BeTrue("new messages (4) are within threshold (5)");
    }

    [Fact]
    public void IsValidFor_WithNewMessagesExceedingThreshold_ShouldReturnFalse()
    {
        // Arrange
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, targetCount: 20, reductionThreshold: 5);

        // Act - 6 new messages (exceeds threshold of 5)
        var isValid = reduction.IsValidFor(106, HistoryCountingUnit.Messages);

        // Assert
        isValid.Should().BeFalse("new messages (6) exceed threshold (5)");
    }

    [Fact]
    public void IsValidFor_WithFewerMessages_ShouldReturnFalse()
    {
        // Arrange
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages);

        // Act - Messages deleted
        var isValid = reduction.IsValidFor(95, HistoryCountingUnit.Messages);

        // Assert
        isValid.Should().BeFalse("messages were deleted (invalidates cache)");
    }

    [Fact]
    public void IsValidFor_WithZeroThreshold_ShouldRequireExactCount()
    {
        // Arrange
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, reductionThreshold: 0);

        // Act
        var validSame = reduction.IsValidFor(100, HistoryCountingUnit.Messages);
        var invalidMore = reduction.IsValidFor(101, HistoryCountingUnit.Messages);

        // Assert
        validSame.Should().BeTrue();
        invalidMore.Should().BeFalse("threshold is 0, requires exact count");
    }

    [Fact]
    public void IsValidFor_WithMismatchedUnit_ShouldReturnFalse()
    {
        // Arrange — created with Messages unit
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, countingUnit: HistoryCountingUnit.Messages);

        // Act — queried with Exchanges unit
        var isValid = reduction.IsValidFor(100, HistoryCountingUnit.Exchanges);

        // Assert
        isValid.Should().BeFalse("counting unit changed, cache is invalid");
    }

    [Fact]
    public void IsValidFor_Exchanges_WithNewExchangesWithinThreshold_ShouldReturnTrue()
    {
        // Arrange — reduction created at exchange 20
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(
            messages,
            countAtReduction: 20,
            reductionThreshold: 5,
            countingUnit: HistoryCountingUnit.Exchanges);

        // Act — currently at exchange 24 (4 new, within threshold of 5)
        var isValid = reduction.IsValidFor(24, HistoryCountingUnit.Exchanges);

        // Assert
        isValid.Should().BeTrue("4 new exchanges within threshold of 5");
    }

    [Fact]
    public void IsValidFor_Exchanges_WithNewExchangesExceedingThreshold_ShouldReturnFalse()
    {
        // Arrange — reduction created at exchange 20
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(
            messages,
            countAtReduction: 20,
            reductionThreshold: 5,
            countingUnit: HistoryCountingUnit.Exchanges);

        // Act — currently at exchange 26 (6 new, exceeds threshold of 5)
        var isValid = reduction.IsValidFor(26, HistoryCountingUnit.Exchanges);

        // Assert
        isValid.Should().BeFalse("6 new exchanges exceed threshold of 5");
    }

    #endregion

    #region ValidateIntegrity Tests

    [Fact]
    public void ValidateIntegrity_WithUnchangedMessages_ShouldReturnTrue()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, summarizedUpToIndex: 90);
        reduction.ValidateIntegrity(messages).Should().BeTrue("messages have not changed");
    }

    [Fact]
    public void ValidateIntegrity_WithModifiedMessage_ShouldReturnFalse()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, summarizedUpToIndex: 90);

        var modifiedMessages = messages.ToList();
        modifiedMessages[50] = new ChatMessage(ChatRole.User, "MODIFIED MESSAGE");

        reduction.ValidateIntegrity(modifiedMessages).Should().BeFalse("message content changed");
    }

    [Fact]
    public void ValidateIntegrity_WithReorderedMessages_ShouldReturnFalse()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, summarizedUpToIndex: 90);

        var reorderedMessages = messages.ToList();
        (reorderedMessages[10], reorderedMessages[20]) = (reorderedMessages[20], reorderedMessages[10]);

        reduction.ValidateIntegrity(reorderedMessages).Should().BeFalse("message order changed");
    }

    [Fact]
    public void ValidateIntegrity_WithDeletedMessages_ShouldReturnFalse()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, summarizedUpToIndex: 90);

        var deletedMessages = messages.Take(50).Concat(messages.Skip(51)).ToList();

        reduction.ValidateIntegrity(deletedMessages).Should().BeFalse("messages were deleted");
    }

    [Fact]
    public void ValidateIntegrity_WithNewMessagesAppended_ShouldReturnTrue()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, summarizedUpToIndex: 90);

        var newMessages = messages.ToList();
        newMessages.Add(new ChatMessage(ChatRole.User, "New message 100"));

        reduction.ValidateIntegrity(newMessages).Should().BeTrue("appended messages don't affect summarized range");
    }

    [Fact]
    public void ValidateIntegrity_WithFewerMessagesThanSummarized_ShouldThrow()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, summarizedUpToIndex: 90);

        var tooFewMessages = messages.Take(50).ToList();

        var act = () => reduction.ValidateIntegrity(tooFewMessages);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Message count*less than SummarizedUpToIndex*");
    }

    #endregion

    #region ApplyToMessages Tests

    [Fact]
    public void ApplyToMessages_WithoutSystemMessage_ShouldReturnSummaryAndRecentMessages()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, summarizedUpToIndex: 90);

        var reduced = reduction.ApplyToMessages(messages).ToList();

        reduced.Should().HaveCount(11); // 1 summary + 10 recent (90-99)
        reduced[0].Role.Should().Be(ChatRole.Assistant);
        reduced[0].Text.Should().Be("Summary of old messages");
        reduced[1].Text.Should().Be("Message 90");
        reduced[10].Text.Should().Be("Message 99");
    }

    [Fact]
    public void ApplyToMessages_WithSystemMessage_ShouldPrependSystem()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, summarizedUpToIndex: 90);
        var systemMessage = new ChatMessage(ChatRole.System, "You are a helpful assistant");

        var reduced = reduction.ApplyToMessages(messages, systemMessage).ToList();

        reduced.Should().HaveCount(12); // 1 system + 1 summary + 10 recent
        reduced[0].Role.Should().Be(ChatRole.System);
        reduced[1].Role.Should().Be(ChatRole.Assistant);
        reduced[1].Text.Should().Be("Summary of old messages");
        reduced[2].Text.Should().Be("Message 90");
    }

    [Fact]
    public void ApplyToMessages_WithModifiedMessages_ShouldThrow()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, summarizedUpToIndex: 90);

        var modifiedMessages = messages.ToList();
        modifiedMessages[50] = new ChatMessage(ChatRole.User, "MODIFIED");

        var act = () => reduction.ApplyToMessages(modifiedMessages).ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*integrity check failed*");
    }

    [Fact]
    public void ApplyToMessages_WithNewMessagesAppended_ShouldIncludeThem()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, summarizedUpToIndex: 90);

        var newMessages = messages.ToList();
        newMessages.Add(new ChatMessage(ChatRole.User, "Message 100"));
        newMessages.Add(new ChatMessage(ChatRole.User, "Message 101"));

        var reduced = reduction.ApplyToMessages(newMessages).ToList();

        reduced.Should().HaveCount(13); // 1 summary + 12 recent (90-101)
        reduced[11].Text.Should().Be("Message 100");
        reduced[12].Text.Should().Be("Message 101");
    }

    [Fact]
    public void ApplyToMessages_WithAllMessagesSummarized_ShouldReturnOnlySummary()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, summarizedUpToIndex: 100);

        var reduced = reduction.ApplyToMessages(messages).ToList();

        reduced.Should().HaveCount(1);
        reduced[0].Text.Should().Be("Summary of old messages");
    }

    #endregion

    #region ExchangeCount Tests

    [Fact]
    public void HistoryReductionStateData_ExchangeCount_DefaultsToZero()
    {
        var state = new HistoryReductionStateData();
        state.ExchangeCount.Should().Be(0);
    }

    [Fact]
    public void WithIncrementedExchangeCount_ShouldIncrementByOne()
    {
        var state = new HistoryReductionStateData();

        var after1 = state.WithIncrementedExchangeCount();
        var after2 = after1.WithIncrementedExchangeCount();
        var after3 = after2.WithIncrementedExchangeCount();

        after1.ExchangeCount.Should().Be(1);
        after2.ExchangeCount.Should().Be(2);
        after3.ExchangeCount.Should().Be(3);
    }

    [Fact]
    public void WithIncrementedExchangeCount_ShouldNotMutateOriginal()
    {
        var original = new HistoryReductionStateData();
        var incremented = original.WithIncrementedExchangeCount();

        original.ExchangeCount.Should().Be(0, "original is immutable");
        incremented.ExchangeCount.Should().Be(1);
    }

    [Fact]
    public void WithIncrementedExchangeCount_ShouldPreserveLastReduction()
    {
        var messages = CreateTestMessages(10);
        var reduction = CreateSampleReduction(messages);
        var state = new HistoryReductionStateData().WithReduction(reduction);

        var incremented = state.WithIncrementedExchangeCount();

        incremented.LastReduction.Should().BeSameAs(reduction, "reduction is preserved");
        incremented.ExchangeCount.Should().Be(1);
    }

    #endregion

    #region Immutability Tests

    [Fact]
    public void CachedReduction_ShouldBeImmutable()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages);

        var modified = reduction with { SummaryContent = "New summary" };

        reduction.SummaryContent.Should().Be("Summary of old messages", "original unchanged");
        modified.SummaryContent.Should().Be("New summary");
        reduction.Should().NotBeSameAs(modified);
    }

    #endregion

    #region Hash Consistency Tests

    [Fact]
    public void MessageHash_ShouldBeConsistent_ForSameMessages()
    {
        var messages1 = CreateTestMessages(100);
        var messages2 = CreateTestMessages(100);

        var reduction1 = CreateSampleReduction(messages1);
        var reduction2 = CreateSampleReduction(messages2);

        reduction1.MessageHash.Should().Be(reduction2.MessageHash,
            "same message content should produce same hash");
    }

    [Fact]
    public void MessageHash_ShouldBeDifferent_ForDifferentMessages()
    {
        var messages1 = CreateTestMessages(100);
        var messages2 = CreateTestMessages(100);
        messages2[50] = new ChatMessage(ChatRole.User, "DIFFERENT");

        var reduction1 = CreateSampleReduction(messages1);
        var reduction2 = CreateSampleReduction(messages2);

        reduction1.MessageHash.Should().NotBe(reduction2.MessageHash);
    }

    [Fact]
    public void MessageHash_ShouldBeDifferent_WhenOrderChanges()
    {
        var messages1 = CreateTestMessages(100);
        var messages2 = messages1.ToList();
        (messages2[10], messages2[20]) = (messages2[20], messages2[10]);

        var reduction1 = CreateSampleReduction(messages1);
        var reduction2 = CreateSampleReduction(messages2);

        reduction1.MessageHash.Should().NotBe(reduction2.MessageHash);
    }

    #endregion

    #region Integration Scenarios

    [Fact]
    public void Scenario_ExchangeCountingCacheHitAcrossMultipleTurns()
    {
        // Reduction created at exchange 20, threshold 5
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(
            messages,
            reductionThreshold: 5,
            countAtReduction: 20,
            countingUnit: HistoryCountingUnit.Exchanges);

        // Exchange 24 — within threshold
        reduction.IsValidFor(24, HistoryCountingUnit.Exchanges).Should().BeTrue();

        // Exchange 25 — at threshold boundary (still valid: 25-20=5 <= 5)
        reduction.IsValidFor(25, HistoryCountingUnit.Exchanges).Should().BeTrue();

        // Exchange 26 — over threshold
        reduction.IsValidFor(26, HistoryCountingUnit.Exchanges).Should().BeFalse();
    }

    [Fact]
    public void Scenario_MessageCountingCacheHitAcrossMultipleTurns()
    {
        var messages = CreateTestMessages(100);
        var reduction = CreateSampleReduction(messages, reductionThreshold: 10);

        var turn2Messages = messages.ToList();
        for (int i = 100; i < 105; i++)
            turn2Messages.Add(new ChatMessage(ChatRole.User, $"Message {i}"));

        reduction.IsValidFor(turn2Messages.Count, HistoryCountingUnit.Messages).Should().BeTrue();
        reduction.ValidateIntegrity(turn2Messages).Should().BeTrue();

        var reduced = reduction.ApplyToMessages(turn2Messages).ToList();
        reduced.Should().HaveCount(16); // 1 summary + 15 recent (90-104)
    }

    [Fact]
    public void Scenario_SerializationRoundTrip()
    {
        var messages = CreateTestMessages(100);
        var original = CreateSampleReduction(messages, countAtReduction: 42, countingUnit: HistoryCountingUnit.Exchanges);

        var copy = original with { };

        copy.SummarizedUpToIndex.Should().Be(original.SummarizedUpToIndex);
        copy.CountAtReduction.Should().Be(original.CountAtReduction);
        copy.SummaryContent.Should().Be(original.SummaryContent);
        copy.CreatedAt.Should().Be(original.CreatedAt);
        copy.MessageHash.Should().Be(original.MessageHash);
        copy.TargetCount.Should().Be(original.TargetCount);
        copy.ReductionThreshold.Should().Be(original.ReductionThreshold);
        copy.CountingUnit.Should().Be(original.CountingUnit);
    }

    #endregion
}
