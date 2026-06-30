// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using FluentAssertions;
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Evaluations.Tests.Infrastructure;

namespace HPD.Agent.Evaluations.Tests.Integration;

public sealed class AgentRunConfigEvalExtensionsTests
{
    [Fact]
    public void WithAdditionalEvaluators_ReplacesPreviousList()
    {
        var first = new StubDeterministicEvaluator("first");
        var second = new StubDeterministicEvaluator("second");
        var config = new AgentRunConfig()
            .WithAdditionalEvaluators(first)
            .WithAdditionalEvaluators(second);

        config.AdditionalEvaluators.Should().NotBeNull();
        config.AdditionalEvaluators!.Should().ContainSingle()
            .Which.Should().BeSameAs(second);
    }

    [Fact]
    public void AdditionalEvaluators_DefaultsToNull()
    {
        var config = new AgentRunConfig();

        config.AdditionalEvaluators.Should().BeNull();
    }

    [Fact]
    public void WithEvaluatorSamplingOverride_StoresSamplingRate()
    {
        var config = new AgentRunConfig()
            .WithEvaluatorSamplingOverride(0.25);

        config.EvaluatorSamplingOverride.Should().Be(0.25);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void WithEvaluatorSamplingOverride_RejectsOutOfRange(double samplingRate)
    {
        var config = new AgentRunConfig();

        var act = () => config.WithEvaluatorSamplingOverride(samplingRate);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithEvalJudgeConfigOverride_RoundTripsTypedConfig()
    {
        var judgeConfig = new EvalJudgeConfig
        {
            TimeoutSeconds = 17,
            OverrideChatClient = new FakeJudgeChatClient(),
        };

        var config = new AgentRunConfig()
            .WithEvalJudgeConfigOverride(judgeConfig);

        config.EvalJudgeConfigOverride.Should().BeSameAs(judgeConfig);
    }
}
