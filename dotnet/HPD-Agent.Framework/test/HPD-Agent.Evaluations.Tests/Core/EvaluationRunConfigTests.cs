// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using FluentAssertions;
using HPD.Agent.Evaluations.Tests.Integration;
using HPD.Agent.Evaluations.Tests.Infrastructure;

namespace HPD.Agent.Evaluations.Tests.Core;

public sealed class EvaluationRunConfigTests
{
    [Fact]
    public void Snapshot_CopiesOwnedCollectionsAndRetainsEvaluatorIdentity()
    {
        var evaluator = new StubDeterministicEvaluator("test");
        var source = new EvaluationRunConfig
        {
            SamplingRate = 0.25,
            AdditionalEvaluators = [evaluator],
            Judge = new EvaluationJudgeRunConfig { TimeoutSeconds = 17 }
        };

        var snapshot = source.Snapshot().Should().BeOfType<EvaluationRunConfig>().Subject;

        snapshot.Should().NotBeSameAs(source);
        snapshot.AdditionalEvaluators.Should().NotBeSameAs(source.AdditionalEvaluators);
        snapshot.AdditionalEvaluators.Should().ContainSingle().Which.Should().BeSameAs(evaluator);
        snapshot.Judge.Should().NotBeSameAs(source.Judge);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void SamplingRate_RejectsOutOfRangeValues(double value)
    {
        var act = () => new EvaluationRunConfig { SamplingRate = value };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
