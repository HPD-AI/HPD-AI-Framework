// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent.Audio;
using Xunit;

namespace HPD.Agent.Tests.Audio;

/// <summary>
/// Unit tests for HeuristicTurnDetector.
/// </summary>
public class HeuristicTurnDetectorTests
{
    private readonly HeuristicTurnDetector _detector = new();

    [Theory]
    [InlineData("Hello, how are you?", 0.9f)]
    [InlineData("I'm doing great!", 0.9f)]
    [InlineData("That's interesting.", 0.9f)]
    public void GetCompletionProbability_StrongEndings_ReturnsHighProbability(string text, float expectedMin)
    {
        // Act
        var probability = _detector.GetCompletionProbability(text);

        // Assert
        Assert.True(probability >= expectedMin, $"Expected >= {expectedMin}, got {probability}");
    }

    [Theory]
    [InlineData("Well, I think", 0.3f)]
    [InlineData("First of all,", 0.3f)]
    [InlineData("The reason is;", 0.3f)]
    public void GetCompletionProbability_WeakEndings_ReturnsMediumProbability(string text, float expectedMax)
    {
        // Act
        var probability = _detector.GetCompletionProbability(text);

        // Assert
        Assert.True(probability <= expectedMax, $"Expected <= {expectedMax}, got {probability}");
    }

    [Theory]
    [InlineData("I was thinking", 0.2f)]  // No punctuation
    [InlineData("Maybe we could", 0.2f)]  // No punctuation
    public void GetCompletionProbability_NoPunctuation_ReturnsLowProbability2(string text, float expectedMax)
    {
        // Act
        var probability = _detector.GetCompletionProbability(text);

        // Assert
        Assert.True(probability <= expectedMax, $"Expected <= {expectedMax}, got {probability}");
    }

    [Theory]
    [InlineData("So I was thinking about", 0.2f)]
    [InlineData("The main reason", 0.2f)]
    [InlineData("Actually", 0.2f)]
    public void GetCompletionProbability_NoPunctuation_ReturnsVeryLowProbability(string text, float expectedMax)
    {
        // Act
        var probability = _detector.GetCompletionProbability(text);

        // Assert
        Assert.True(probability <= expectedMax, $"Expected <= {expectedMax}, got {probability}");
    }

    [Theory]
    [InlineData("He said \"hello\"", 0.7f)]
    [InlineData("She replied 'yes'", 0.7f)]
    public void GetCompletionProbability_QuotationEndings_ReturnsModerateProbability(string text, float expectedMin)
    {
        // Act
        var probability = _detector.GetCompletionProbability(text);

        // Assert
        Assert.True(probability >= expectedMin, $"Expected >= {expectedMin}, got {probability}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void GetCompletionProbability_EmptyOrNull_ReturnsZero(string? text)
    {
        // Act
        var probability = _detector.GetCompletionProbability(text!);

        // Assert
        Assert.Equal(0.0f, probability);
    }

    [Fact]
    public void Reset_DoesNotThrow()
    {
        // Act & Assert - should not throw
        _detector.Reset();
    }

    [Fact]
    public void GetCompletionProbability_MultipleCallsAreDeterministic()
    {
        // Arrange
        var text = "How are you today?";

        // Act
        var prob1 = _detector.GetCompletionProbability(text);
        var prob2 = _detector.GetCompletionProbability(text);

        // Assert
        Assert.Equal(prob1, prob2);
    }

    //
    // NEW TESTS: Trailing Word Detection
    //

    [Theory]
    [InlineData("I was thinking and.", 0.6f)]  // Trailing incomplete word despite period
    [InlineData("The reason is but.", 0.6f)]
    [InlineData("Maybe because.", 0.6f)]
    [InlineData("I thought that.", 0.6f)]
    public void GetCompletionProbability_TrailingIncompleteWords_ReturnsMediumProbability(string text, float expected)
    {
        // Act
        var probability = _detector.GetCompletionProbability(text);

        // Assert
        Assert.Equal(expected, probability);
    }

    [Theory]
    [InlineData("I completely agree.", 0.9f)]  // No trailing incomplete word
    [InlineData("That sounds good!", 0.9f)]
    [InlineData("I'll do it?", 0.9f)]
    public void GetCompletionProbability_NoTrailingIncompleteWords_ReturnsHighProbability(string text, float expectedMin)
    {
        // Act
        var probability = _detector.GetCompletionProbability(text);

        // Assert
        Assert.True(probability >= expectedMin, $"Expected >= {expectedMin}, got {probability}");
    }

    [Theory]
    [InlineData("and")]  // Single trailing word with period implied
    [InlineData("but")]
    public void GetCompletionProbability_SingleTrailingWord_DoesNotCrash(string text)
    {
        // Arrange
        var textWithPeriod = text + ".";

        // Act
        var probability = _detector.GetCompletionProbability(textWithPeriod);

        // Assert - should not crash, just return normal period probability
        Assert.True(probability > 0);
    }
}
