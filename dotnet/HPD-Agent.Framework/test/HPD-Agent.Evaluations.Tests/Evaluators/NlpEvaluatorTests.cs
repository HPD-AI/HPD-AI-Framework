// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using FluentAssertions;
using HPD.Agent.Evaluations.Evaluators.Nlp;
using HPD.Agent.Evaluations.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Tests.Evaluators;

public sealed class NlpEvaluatorTests
{
    private static ChatResponse Respond(string text) =>
        new([new ChatMessage(ChatRole.Assistant, text)]);

    [Fact]
    public async Task Bleu_IdenticalReference_ReturnsHighScore()
    {
        var result = await new BleuEvaluator("the cat is on the mat")
            .EvaluateAsync([], Respond("the cat is on the mat"));

        result.ShouldHaveNumericMetricInRange("BLEU", 0.99, 1.0)
            .ShouldBeMarkedAsBuiltIn();
    }

    [Fact]
    public async Task Gleu_IdenticalReference_ReturnsHighScore()
    {
        var result = await new GleuEvaluator("the cat is on the mat")
            .EvaluateAsync([], Respond("the cat is on the mat"));

        result.ShouldHaveNumericMetricInRange("GLEU", 0.99, 1.0)
            .ShouldBeMarkedAsBuiltIn();
    }

    [Fact]
    public async Task TextF1_PartialOverlap_ReturnsExpectedScore()
    {
        var result = await new TextF1Evaluator("alpha beta gamma")
            .EvaluateAsync([], Respond("alpha beta delta"));

        result.ShouldHaveNumericMetricInRange("F1", 0.65, 0.68)
            .ShouldBeMarkedAsBuiltIn();
    }

    [Fact]
    public async Task RougeL_SubsequenceOverlap_ReturnsRecall()
    {
        var result = await new RougeEvaluator("alpha beta gamma delta", RougeVariant.RougeL)
            .EvaluateAsync([], Respond("alpha gamma delta"));

        result.ShouldHaveNumericMetricInRange("ROUGE", 0.74, 0.76)
            .ShouldBeMarkedAsBuiltIn();
    }

    [Fact]
    public async Task Rouge2_BigramOverlap_ReturnsRecall()
    {
        var result = await new RougeEvaluator("alpha beta gamma delta", RougeVariant.Rouge2)
            .EvaluateAsync([], Respond("alpha beta delta"));

        result.ShouldHaveNumericMetricInRange("ROUGE", 0.32, 0.34);
    }

    [Fact]
    public async Task RougeS_SkipBigramOverlap_ReturnsRecall()
    {
        var result = await new RougeEvaluator("alpha beta gamma", RougeVariant.RougeS)
            .EvaluateAsync([], Respond("alpha gamma beta"));

        result.ShouldHaveNumericMetricInRange("ROUGE", 0.66, 0.67);
    }

    [Fact]
    public async Task Meteor_StemmedOverlap_ReturnsHighScore()
    {
        var result = await new MeteorEvaluator("systems running")
            .EvaluateAsync([], Respond("system run"));

        result.ShouldHaveNumericMetricInRange("METEOR", 0.93, 0.94)
            .ShouldBeMarkedAsBuiltIn();
    }

    [Fact]
    public async Task Meteor_MultipleReferences_UsesBestScore()
    {
        var result = await new MeteorEvaluator(
                "the dog ran through the park",
                "the cat sat on the mat")
            .EvaluateAsync([], Respond("the cat sat on the mat"));

        result.ShouldHaveNumericMetricInRange("METEOR", 0.99, 1.0);
    }

    [Fact]
    public async Task Meteor_FragmentationPenalty_LowersScrambledOutput()
    {
        var ordered = await new MeteorEvaluator("alpha beta gamma delta")
            .EvaluateAsync([], Respond("alpha beta gamma delta"));
        var scrambled = await new MeteorEvaluator("alpha beta gamma delta")
            .EvaluateAsync([], Respond("delta gamma beta alpha"));

        ordered.ShouldHaveNumericMetricInRange("METEOR", 0.99, 1.0);
        scrambled.ShouldHaveNumericMetricInRange("METEOR", 0.49, 0.51);
    }

    [Fact]
    public async Task Meteor_NoOverlap_ReturnsZero()
    {
        var result = await new MeteorEvaluator("alpha beta")
            .EvaluateAsync([], Respond("gamma delta"));

        result.ShouldHaveNumericMetricInRange("METEOR", 0.0, 0.0);
    }
}
