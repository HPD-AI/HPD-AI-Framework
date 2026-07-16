using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Core;

public class ThreadContextUsageEstimatorTests
{
    [Fact]
    public async Task EstimateAsync_FallsBackToRoughMessageEstimate()
    {
        var thread = new Thread("session-1", "main");
        thread.AddMessage(new ChatMessage(ChatRole.User, "12345678"));
        thread.AddMessage(new ChatMessage(ChatRole.Assistant, "12345678"));
        var estimator = new ThreadContextUsageEstimator();

        var usage = await estimator.EstimateAsync(thread, new AgentRunConfig());

        usage.LastObservedInputTokens.Should().BeNull();
        usage.EstimatedInputTokens.Should().Be(4);
        usage.EffectiveInputTokens.Should().Be(4);
        usage.UsageRatio.Should().BeNull();
        usage.IsEstimate.Should().BeTrue();
        usage.Source.Should().Be("rough-message-estimate");
    }

    [Fact]
    public async Task EstimateAsync_WithoutModelContext_ReturnsUsageWithoutRatio()
    {
        var thread = new Thread("session-1", "main");
        thread.AddMessage(new ChatMessage(ChatRole.User, "12345678"));
        var estimator = new ThreadContextUsageEstimator();

        var usage = await estimator.EstimateAsync(thread, new AgentRunConfig());

        usage.ContextWindow.Should().BeNull();
        usage.EffectiveInputTokens.Should().Be(2);
        usage.UsageRatio.Should().BeNull();
    }
}
