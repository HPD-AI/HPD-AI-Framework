using System.Collections.Immutable;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Core;

public class ThreadContextUsageEstimatorTests
{
    [Fact]
    public async Task EstimateAsync_UsesLastObservedProviderUsageWhenAvailable()
    {
        var thread = new Thread("session-1", "main");
        thread.AddMessage(new ChatMessage(ChatRole.User, "rough estimate should not win"));
        var state = new CompactionStateData
        {
            LastTurnUsage = new UsageDetails { InputTokenCount = 900 },
            LastIterationUsage = ImmutableList<UsageDetails?>.Empty
        };
        thread.SetMiddlewareState(
            typeof(CompactionStateData).FullName!,
            JsonSerializer.Serialize(state, SessionJsonContext.Default.CompactionStateData));
        var estimator = new ThreadContextUsageEstimator();

        var usage = await estimator.EstimateAsync(thread, RunConfigWithContextWindow(1_000));

        usage.LastObservedInputTokens.Should().Be(900);
        usage.EstimatedInputTokens.Should().BeNull();
        usage.EffectiveInputTokens.Should().Be(900);
        usage.UsageRatio.Should().Be(0.9);
        usage.IsEstimate.Should().BeFalse();
        usage.Source.Should().Be("last-observed-provider-usage");
    }

    [Fact]
    public async Task EstimateAsync_FallsBackToRoughMessageEstimate()
    {
        var thread = new Thread("session-1", "main");
        thread.AddMessage(new ChatMessage(ChatRole.User, "12345678"));
        thread.AddMessage(new ChatMessage(ChatRole.Assistant, "12345678"));
        var estimator = new ThreadContextUsageEstimator();

        var usage = await estimator.EstimateAsync(thread, RunConfigWithContextWindow(16));

        usage.LastObservedInputTokens.Should().BeNull();
        usage.EstimatedInputTokens.Should().Be(4);
        usage.EffectiveInputTokens.Should().Be(4);
        usage.UsageRatio.Should().Be(0.25);
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

    private static AgentRunConfig RunConfigWithContextWindow(int contextWindow) =>
        new()
        {
            Compaction = new CompactionRunConfig
            {
                ModelContext = new ModelContextWindowOptions
                {
                    ProviderKey = "openai",
                    ModelId = "model",
                    ContextWindow = contextWindow
                }
            }
        };
}
