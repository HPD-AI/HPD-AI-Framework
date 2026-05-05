// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Evaluators.Deterministic;
using HPD.Agent.Evaluations.RedTeam;
using HPD.Agent.Evaluations.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Tests.RedTeam;

public sealed class RedTeamRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_GeneratesStrategiesAndComputesReportWithoutStore()
    {
        var agent = new FixedResponseAgent("safe response");

        var report = await RedTeamRunner.ExecuteAsync(
            agent,
            new RedTeamRunOptions
            {
                CasesPerPlugin = 2,
                Plugins = [new PromptInjectionPlugin()],
                Strategies = [new BasicStrategy(), new Rot13Strategy()],
                GlobalEvaluators = [new OutputContainsEvaluator("not present")],
                ExperimentName = "redteam-local",
            });

        report.Cases.Should().HaveCount(4);
        report.EvaluationReport.Cases.Should().HaveCount(4);
        report.AttackSuccessRate.Should().Be(1.0);
        report.AttackSuccessRateByPlugin["prompt-injection"].Should().Be(1.0);
        report.AttackSuccessRateByStrategy["basic"].Should().Be(1.0);
        report.AttackSuccessRateByStrategy["rot13"].Should().Be(1.0);
        report.Findings.Should().HaveCount(4);
        agent.Configs.Should().HaveCount(4);
    }

    [Fact]
    public async Task ExecuteAsync_NoStrategies_DefaultsToBasicStrategy()
    {
        var agent = new FixedResponseAgent("safe response");

        var report = await RedTeamRunner.ExecuteAsync(
            agent,
            new RedTeamRunOptions
            {
                CasesPerPlugin = 1,
                Plugins = [new SecretLeakPlugin()],
                GlobalEvaluators = [new OutputContainsEvaluator("safe response")],
                ExperimentName = "redteam-basic",
            });

        report.Cases.Should().ContainSingle()
            .Which.StrategyId.Should().Be("basic");
        report.AttackSuccessRate.Should().Be(0.0);
        report.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_PersistResults_UsesOnlyCurrentExperimentForStoreBackedReport()
    {
        var agent = new FixedResponseAgent("safe response");
        var store = new InMemoryScoreStore();
        await store.WriteScoreAsync(new ScoreRecord
        {
            Id = "unrelated",
            EvaluatorName = "Other",
            EvaluatorVersion = "1.0.0",
            Result = new EvaluationResult(new BooleanMetric("Other") { Value = false }),
            Source = EvaluationSource.Test,
            SessionId = "other-redteam-run",
            BranchId = "branch",
            TurnIndex = 0,
            AgentName = "TestAgent",
            Policy = EvalPolicy.MustAlwaysPass,
            CreatedAt = DateTimeOffset.UtcNow,
            RedTeamPluginId = "other-plugin",
            RedTeamStrategyId = "basic",
            RedTeamCategory = "DataLeakage",
            RedTeamSeverity = "Critical",
            AttackGoal = "unrelated",
            AttackSucceeded = true,
        });

        var report = await RedTeamRunner.ExecuteAsync(
            agent,
            new RedTeamRunOptions
            {
                CasesPerPlugin = 1,
                Plugins = [new PromptInjectionPlugin()],
                Strategies = [new BasicStrategy()],
                GlobalEvaluators = [new OutputContainsEvaluator("not present")],
                ExperimentName = "current-redteam-run",
                RunOptions = new RunEvalsOptions<string>
                {
                    PersistResults = true,
                    ScoreStore = store,
                },
            });

        report.AttackSuccessRate.Should().Be(1.0);
        report.AttackSuccessRateByPlugin.Should().ContainSingle();
        report.AttackSuccessRateByPlugin.Should().ContainKey("prompt-injection");
        report.AttackSuccessRateByPlugin.Should().NotContainKey("other-plugin");
        report.Findings.Should().ContainSingle()
            .Which.SessionId.Should().Be("current-redteam-run");
    }

    private sealed class FixedResponseAgent(string responseText) : IAgent
    {
        public List<AgentRunConfig> Configs { get; } = [];

        public Task<ChatResponse> RunAsync(AgentRunConfig config, CancellationToken ct = default)
        {
            Configs.Add(config);
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)]));
        }
    }
}
