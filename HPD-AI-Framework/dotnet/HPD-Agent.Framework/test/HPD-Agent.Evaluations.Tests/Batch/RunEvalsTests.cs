// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Evaluations.Evaluators.Deterministic;
using HPD.Agent.Evaluations.Evaluators.LlmJudge;
using HPD.Agent.Evaluations.RedTeam;
using HPD.Agent.Evaluations.Storage;
using HPD.Agent.Evaluations.Tests.Infrastructure;
using HPD.Agent.Evaluations.Tests.Integration;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Evaluations.Tests.Batch;

public sealed class RunEvalsTests
{
    [Fact]
    public async Task ExecuteAsync_BaseRunConfig_IsCopiedAndEvaluatorsDisabled()
    {
        var agent = new CapturingAgent();
        var dataset = SingleCaseDataset("hello");
        var baseConfig = new AgentRunConfig
        {
            ProviderKey = "openai",
            ModelId = "gpt-test",
            ProviderOptions = JsonDocument.Parse("""{"reasoningEffort":"high"}""").RootElement.Clone(),
            DisableEvaluators = false,
            ContextOverrides = new() { ["tenant"] = "alpha" },
        };

        var report = await RunEvals.ExecuteAsync(
            agent,
            dataset,
            [new OutputContainsEvaluator("response")],
            new RunEvalsOptions<string> { BaseRunConfig = baseConfig },
            experimentName: "exp");

        agent.Configs.Should().ContainSingle();
        agent.Configs[0].Should().NotBeSameAs(baseConfig);
        agent.Configs[0].ProviderKey.Should().Be("openai");
        agent.Configs[0].ModelId.Should().Be("gpt-test");
        agent.Configs[0].GetProviderOptionsRawJson().Should().Be("""{"reasoningEffort":"high"}""");
        agent.Configs[0].DisableEvaluators.Should().BeTrue();
        agent.Configs[0].ContextOverrides.Should().ContainKey("tenant");

        var reportCase = report.Cases.Should().ContainSingle().Subject;
        reportCase.ProviderKey.Should().Be("openai");
        reportCase.ModelId.Should().Be("gpt-test");
        reportCase.ResponseModelId.Should().Be(CapturingAgent.ResponseModelId);
    }

    [Fact]
    public async Task ExecuteAsync_OnCaseComplete_ReceivesOriginalEvalCase()
    {
        var agent = new CapturingAgent();
        EvalCase<string>? callbackCase = null;
        EvaluationReport? callbackReport = null;
        var evalCase = new EvalCase<string>
        {
            CaseId = "case-001",
            Name = "case-1",
            Version = "2",
            ValidFrom = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            Input = "hello",
            GroundTruth = "response to hello",
            Metadata = new Dictionary<string, object> { ["difficulty"] = "easy" },
        };

        await RunEvals.ExecuteAsync(
            agent,
            new Dataset<string> { Cases = [evalCase] },
            [new OutputContainsEvaluator("response")],
            new RunEvalsOptions<string>
            {
                OnCaseComplete = (completedCase, report) =>
                {
                    callbackCase = completedCase;
                    callbackReport = report;
                },
            },
            experimentName: "typed-callback");

        callbackCase.Should().BeSameAs(evalCase);
        callbackCase!.Input.Should().Be("hello");
        callbackCase.GroundTruth.Should().Be("response to hello");
        callbackCase.Metadata.Should().ContainKey("difficulty");
        callbackCase.CaseId.Should().Be("case-001");
        callbackCase.Version.Should().Be("2");
        callbackReport.Should().NotBeNull();
        callbackReport!.Cases.Should().ContainSingle()
            .Which.Name.Should().Be("case-1");
    }

    [Fact]
    public async Task ExecuteAsync_PersistResults_WritesTestScoreRecords()
    {
        var agent = new CapturingAgent();
        var store = new InMemoryScoreStore();
        var evaluator = new OutputContainsEvaluator("response");
        var validFrom = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var validTo = DateTimeOffset.Parse("2026-02-01T00:00:00Z");

        await RunEvals.ExecuteAsync(
            agent,
            new Dataset<string>
            {
                DatasetId = "support-bench",
                Version = "2026.02",
                Cases =
                [
                    new EvalCase<string>
                    {
                        CaseId = "case-001",
                        Name = "case-1",
                        Version = "2",
                        ValidFrom = validFrom,
                        ValidTo = validTo,
                        Input = "hello",
                    },
                ],
            },
            [evaluator],
            new RunEvalsOptions<string>
            {
                BaseRunConfig = new AgentRunConfig
                {
                    ProviderKey = "openai",
                    ModelId = "gpt-test",
                },
                PersistResults = true,
                ScoreStore = store,
            },
            experimentName: "nightly");

        var records = new List<ScoreRecord>();
        await foreach (var record in store.GetScoresAsync(sessionId: "nightly"))
            records.Add(record);

        records.Should().ContainSingle();
        records[0].Source.Should().Be(EvaluationSource.Test);
        records[0].EvaluatorName.Should().Be(nameof(OutputContainsEvaluator));
        records[0].Policy.Should().Be(EvalPolicy.MustAlwaysPass);
        records[0].DatasetId.Should().Be("support-bench");
        records[0].DatasetVersion.Should().Be("2026.02");
        records[0].CaseId.Should().Be("case-001");
        records[0].CaseVersion.Should().Be("2");
        records[0].CaseValidFrom.Should().Be(validFrom);
        records[0].CaseValidTo.Should().Be(validTo);
        records[0].ProviderKey.Should().Be("openai");
        records[0].ModelId.Should().Be("gpt-test");
        records[0].ResponseModelId.Should().Be(CapturingAgent.ResponseModelId);
    }

    [Fact]
    public async Task ExecuteAsync_DatasetStore_RegistersDatasetVersionBeforeRunning()
    {
        var agent = new CapturingAgent();
        var datasetStore = new InMemoryDatasetStore();

        await RunEvals.ExecuteAsync(
            agent,
            new Dataset<string>
            {
                DatasetId = "support-bench",
                Version = "2026.02",
                Cases =
                [
                    new EvalCase<string>
                    {
                        CaseId = "case-001",
                        Input = "hello",
                        GroundTruth = "response to hello",
                    },
                ],
            },
            [new OutputContainsEvaluator("response")],
            new RunEvalsOptions<string>
            {
                DatasetStore = datasetStore,
                DatasetRegistrationOptions = new()
                {
                    Description = "Nightly support benchmark",
                    RegisteredAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
                },
            },
            experimentName: "nightly");

        var registered = await datasetStore.GetDatasetVersionAsync<string>("support-bench", "2026.02");
        registered.Should().NotBeNull();
        registered!.Cases.Should().ContainSingle().Which.CaseId.Should().Be("case-001");

        var catalog = await datasetStore.GetDatasetAsync("support-bench");
        catalog.Should().NotBeNull();
        catalog!.CurrentVersion.Should().Be("2026.02");
        catalog.Description.Should().Be("Nightly support benchmark");
    }

    [Fact]
    public async Task ExecuteAsync_DatasetStore_SameVersionDifferentContent_FailsBeforeRunningAgent()
    {
        var agent = new CapturingAgent();
        var datasetStore = new InMemoryDatasetStore();
        await datasetStore.RegisterDatasetVersionAsync(new Dataset<string>
        {
            DatasetId = "support-bench",
            Version = "2026.02",
            Cases = [new EvalCase<string> { CaseId = "case-001", Input = "old" }],
        });

        var act = async () => await RunEvals.ExecuteAsync(
            agent,
            new Dataset<string>
            {
                DatasetId = "support-bench",
                Version = "2026.02",
                Cases = [new EvalCase<string> { CaseId = "case-001", Input = "new" }],
            },
            [new OutputContainsEvaluator("response")],
            new RunEvalsOptions<string> { DatasetStore = datasetStore },
            experimentName: "nightly");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already registered with different content*");
        agent.Configs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_DatasetStore_WhenDatasetHasNoIdentity_SkipsRegistration()
    {
        var agent = new CapturingAgent();
        var datasetStore = new InMemoryDatasetStore();

        await RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("hello"),
            [new OutputContainsEvaluator("response")],
            new RunEvalsOptions<string> { DatasetStore = datasetStore },
            experimentName: "anonymous");

        var datasets = new List<DatasetRecord>();
        await foreach (var record in datasetStore.ListDatasetsAsync())
            datasets.Add(record);

        datasets.Should().BeEmpty();
        agent.Configs.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_PersistResults_WritesEvaluationRunRecords()
    {
        var agent = new CapturingAgent();
        var store = new InMemoryScoreStore();

        await RunEvals.ExecuteAsync(
            agent,
            new Dataset<string>
            {
                DatasetId = "support-bench",
                Version = "2026.02",
                Cases =
                [
                    new EvalCase<string>
                    {
                        CaseId = "case-001",
                        Name = "case-1",
                        Version = "2",
                        Input = "hello",
                        Metadata = new Dictionary<string, object> { ["difficulty"] = "easy" },
                    },
                ],
            },
            [new OutputContainsEvaluator("response")],
            new RunEvalsOptions<string>
            {
                BaseRunConfig = new AgentRunConfig
                {
                    ProviderKey = "openai",
                    ModelId = "gpt-test",
                },
                PersistResults = true,
                ScoreStore = store,
                Repeat = 2,
            },
            experimentName: "nightly");

        var runs = new List<EvaluationRunRecord>();
        await foreach (var run in store.GetRunsAsync(executionName: "nightly", scenarioName: "case-1"))
            runs.Add(run);

        runs.Should().HaveCount(2);
        runs.Select(r => r.IterationName).Should().BeEquivalentTo(["1", "2"]);

        var first = runs.Single(r => r.IterationName == "1");
        first.ExecutionName.Should().Be("nightly");
        first.ScenarioName.Should().Be("case-1");
        first.Messages.Should().ContainSingle()
            .Which.Text.Should().Be("hello");
        first.ModelResponse.Text.Should().Be("response to hello");
        first.EvaluationResult.Metrics.Should().ContainKey("Output Contains");
        first.DatasetId.Should().Be("support-bench");
        first.DatasetVersion.Should().Be("2026.02");
        first.CaseId.Should().Be("case-001");
        first.CaseVersion.Should().Be("2");
        first.Metadata.Should().ContainKey("difficulty");
        first.Tags.Should().Contain("dataset:support-bench");
        first.Tags.Should().Contain("case:case-001");
        first.ProviderKey.Should().Be("openai");
        first.ModelId.Should().Be("gpt-test");
        first.ResponseModelId.Should().Be(CapturingAgent.ResponseModelId);
    }

    [Fact]
    public async Task ExecuteAsync_PersistResults_MetadataAbsent_LeavesDatasetProvenanceNull()
    {
        var agent = new CapturingAgent();
        var store = new InMemoryScoreStore();

        await RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("hello"),
            [new OutputContainsEvaluator("response")],
            new RunEvalsOptions<string>
            {
                PersistResults = true,
                ScoreStore = store,
            },
            experimentName: "nightly");

        var records = new List<ScoreRecord>();
        await foreach (var record in store.GetScoresAsync(sessionId: "nightly"))
            records.Add(record);

        records.Should().ContainSingle();
        records[0].DatasetId.Should().BeNull();
        records[0].DatasetVersion.Should().BeNull();
        records[0].CaseId.Should().Be("case-1");
        records[0].CaseVersion.Should().BeNull();
        records[0].CaseValidFrom.Should().BeNull();
        records[0].CaseValidTo.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_PersistResults_SameCaseIdDifferentVersions_PersistDistinctWindows()
    {
        var agent = new CapturingAgent();
        var store = new InMemoryScoreStore();
        var v1From = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var v1To = DateTimeOffset.Parse("2026-02-01T00:00:00Z");
        var v2From = DateTimeOffset.Parse("2026-02-01T00:00:00Z");

        await RunEvals.ExecuteAsync(
            agent,
            new Dataset<string>
            {
                DatasetId = "support-bench",
                Version = "2026.02",
                Cases =
                [
                    new EvalCase<string>
                    {
                        CaseId = "case-001",
                        Version = "1",
                        ValidFrom = v1From,
                        ValidTo = v1To,
                        Input = "hello",
                    },
                    new EvalCase<string>
                    {
                        CaseId = "case-001",
                        Version = "2",
                        ValidFrom = v2From,
                        Input = "hello",
                    },
                ],
            },
            [new OutputContainsEvaluator("response")],
            new RunEvalsOptions<string>
            {
                PersistResults = true,
                ScoreStore = store,
            },
            experimentName: "nightly");

        var records = new List<ScoreRecord>();
        await foreach (var record in store.GetScoresAsync(sessionId: "nightly"))
            records.Add(record);

        records.Should().HaveCount(2);
        records.Select(r => r.CaseId).Should().OnlyContain(id => id == "case-001");
        records.Select(r => r.CaseVersion).Should().BeEquivalentTo(["1", "2"]);
        records.Single(r => r.CaseVersion == "1").CaseValidTo.Should().Be(v1To);
        records.Single(r => r.CaseVersion == "2").CaseValidTo.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_PersistResults_RedTeamCase_PersistsAttackMetadataAndSuccess()
    {
        var agent = new CapturingAgent();
        var store = new InMemoryScoreStore();
        var redTeamCase = (await new PromptInjectionPlugin().GenerateAsync(new RedTeamGenerationContext
        {
            CasesPerPlugin = 1,
        })).Single();

        var strategyCase = (await new BasicStrategy().ApplyAsync([redTeamCase], new RedTeamStrategyContext()))
            .Single();

        await RunEvals.ExecuteAsync(
            agent,
            new Dataset<string>
            {
                DatasetId = "redteam",
                Version = "1",
                Cases = [strategyCase.ToEvalCase()],
            },
            [new OutputContainsEvaluator("this string will not appear")],
            new RunEvalsOptions<string>
            {
                PersistResults = true,
                ScoreStore = store,
            },
            experimentName: "redteam-run");

        var records = new List<ScoreRecord>();
        await foreach (var record in store.GetScoresAsync(sessionId: "redteam-run"))
            records.Add(record);

        var score = records.Should().ContainSingle().Which;
        score.RedTeamPluginId.Should().Be("prompt-injection");
        score.RedTeamStrategyId.Should().Be("basic");
        score.RedTeamCategory.Should().Be("PromptInjection");
        score.RedTeamSeverity.Should().Be("High");
        score.AttackGoal.Should().Be("Override the agent's active instructions.");
        score.AttackSucceeded.Should().BeTrue();

        var attackSuccessRate = await store.GetAttackSuccessRateAsync();
        attackSuccessRate.Should().Be(1.0);
    }

    [Fact]
    public async Task ExecuteAsync_TaskRetryPolicy_RetriesInfrastructureErrors()
    {
        var agent = new CapturingAgent
        {
            FailuresBeforeSuccess = 1,
            FailureFactory = () => new InvalidOperationException("HTTP 429 rate limit"),
        };

        var report = await RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("hello"),
            [new OutputContainsEvaluator("response")],
            new RunEvalsOptions<string>
            {
                TaskRetryPolicy = new ErrorHandlingConfig
                {
                    MaxRetries = 2,
                    RetryDelay = TimeSpan.Zero,
                    MaxRetryDelay = TimeSpan.Zero,
                },
            },
            experimentName: "retry");

        agent.Attempts.Should().Be(2);
        agent.Configs.Should().HaveCount(2);
        agent.Configs.Select(c => c.RuntimeMiddleware?.Count ?? 0)
            .Should().AllBeEquivalentTo(1);
        report.Failures.Should().BeEmpty();
        report.Cases.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_JudgeOverrideChatClient_IsUsedForJudgeCalls()
    {
        var agent = new CapturingAgent();
        var overrideJudge = new FakeJudgeChatClient();
        overrideJudge.EnqueueResponse("<S0>ok</S0><S1>override</S1><S2>true</S2>");

        var report = await RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("hello"),
            [new AspectCriticEvaluator("passes")],
            new RunEvalsOptions<string>
            {
                JudgeConfig = new EvalJudgeConfig { OverrideChatClient = overrideJudge },
            },
            experimentName: "override-chat-client");

        overrideJudge.CallCount.Should().Be(1);
        report.Failures.Should().BeEmpty();
        report.Cases.Should().ContainSingle();
        report.Cases[0].EvaluatorFailures.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_PersistResults_CapturesJudgeCallsOnScoreAndRun()
    {
        var agent = new CapturingAgent();
        var store = new InMemoryScoreStore();
        var judge = new FakeJudgeChatClient();
        judge.EnqueueResponse("<S0>ok</S0><S1>captured</S1><S2>true</S2>");

        await RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("hello"),
            [new AspectCriticEvaluator("passes")],
            new RunEvalsOptions<string>
            {
                PersistResults = true,
                ScoreStore = store,
                JudgeConfig = new EvalJudgeConfig { OverrideChatClient = judge },
            },
            experimentName: "judge-trace");

        var scores = new List<ScoreRecord>();
        await foreach (var score in store.GetScoresAsync(sessionId: "judge-trace"))
            scores.Add(score);

        var scoreRecord = scores.Should().ContainSingle().Which;
        var scoreJudgeCall = scoreRecord.JudgeCalls.Should().ContainSingle().Which;
        scoreJudgeCall.EvaluatorName.Should().Be(nameof(AspectCriticEvaluator));
        scoreJudgeCall.Phase.Should().Be("judge");
        scoreJudgeCall.Succeeded.Should().BeTrue();
        scoreJudgeCall.Prompt.Should().NotBeEmpty();
        scoreJudgeCall.Response!.Text.Should().Contain("<S2>true</S2>");

        var runs = new List<EvaluationRunRecord>();
        await foreach (var run in store.GetRunsAsync(executionName: "judge-trace"))
            runs.Add(run);

        var runRecord = runs.Should().ContainSingle().Which;
        runRecord.JudgeCalls.Should().ContainSingle()
            .Which.Response!.Text.Should().Contain("<S2>true</S2>");
    }

    [Fact]
    public async Task ExecuteAsync_PersistResults_CapturesFailedJudgeCallsOnScoreAndRun()
    {
        var agent = new CapturingAgent();
        var store = new InMemoryScoreStore();
        var judge = new FakeJudgeChatClient();
        judge.ThrowOn(new InvalidOperationException("judge boom"));

        await RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("hello"),
            [new AspectCriticEvaluator("passes")],
            new RunEvalsOptions<string>
            {
                PersistResults = true,
                ScoreStore = store,
                JudgeConfig = new EvalJudgeConfig { OverrideChatClient = judge },
            },
            experimentName: "judge-trace-failure");

        var scores = new List<ScoreRecord>();
        await foreach (var score in store.GetScoresAsync(sessionId: "judge-trace-failure"))
            scores.Add(score);

        var scoreRecord = scores.Should().ContainSingle().Which;
        var scoreJudgeCall = scoreRecord.JudgeCalls.Should().ContainSingle().Which;
        scoreJudgeCall.EvaluatorName.Should().Be(nameof(AspectCriticEvaluator));
        scoreJudgeCall.Succeeded.Should().BeFalse();
        scoreJudgeCall.Response.Should().BeNull();
        scoreJudgeCall.ErrorMessage.Should().Contain("judge boom");

        var runs = new List<EvaluationRunRecord>();
        await foreach (var run in store.GetRunsAsync(executionName: "judge-trace-failure"))
            runs.Add(run);

        runs.Should().ContainSingle().Which.JudgeCalls.Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("judge boom");
    }

    [Fact]
    public async Task ExecuteAsync_JudgeOverrideAgent_MarksInternalEvalJudgeCall()
    {
        var agent = new CapturingAgent();
        var judgeAgent = new CapturingAgent
        {
            ResponseText = "<S0>ok</S0><S1>agent judge</S1><S2>true</S2>",
        };

        var report = await RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("hello"),
            [new AspectCriticEvaluator("passes")],
            new RunEvalsOptions<string>
            {
                JudgeConfig = new EvalJudgeConfig { OverrideAgent = judgeAgent },
            },
            experimentName: "agent-judge");

        report.Failures.Should().BeEmpty();
        report.Cases.Should().ContainSingle();
        report.Cases[0].EvaluatorFailures.Should().BeEmpty();
        judgeAgent.Configs.Should().ContainSingle();
        judgeAgent.Configs[0].IsInternalEvalJudgeCall.Should().BeTrue();
        judgeAgent.Configs[0].DisableEvaluators.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_JudgeOverrideAgent_DoesNotPersistRawPreMiddlewarePrompt()
    {
        var agent = new CapturingAgent();
        var store = new InMemoryScoreStore();
        var judgeAgent = new CapturingAgent
        {
            ResponseText = "<S0>ok</S0><S1>agent judge</S1><S2>true</S2>",
        };

        await RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("email alice@example.com ssn 123-45-6789"),
            [new AspectCriticEvaluator("passes")],
            new RunEvalsOptions<string>
            {
                PersistResults = true,
                ScoreStore = store,
                JudgeConfig = new EvalJudgeConfig { OverrideAgent = judgeAgent },
            },
            experimentName: "agent-judge-trace");

        var scores = new List<ScoreRecord>();
        await foreach (var score in store.GetScoresAsync(sessionId: "agent-judge-trace"))
            scores.Add(score);

        var call = scores.Should().ContainSingle().Which.JudgeCalls.Should().ContainSingle().Which;
        call.Prompt.Should().ContainSingle();
        call.Prompt[0].Text.Should().Contain("raw prompt is not captured");
        call.Prompt[0].Text.Should().NotContain("alice@example.com");
        call.Prompt[0].Text.Should().NotContain("123-45-6789");
        call.Response!.Text.Should().Contain("<S2>true</S2>");
    }

    [Fact]
    public async Task ExecuteAsync_JudgeOverrideAgent_PromptContainsResponseContext()
    {
        var agent = new CapturingAgent();
        var judgeAgent = new CapturingAgent
        {
            ResponseText = "<S0>ok</S0><S1>agent judge</S1><S2>true</S2>",
        };

        await RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("hello"),
            [new AspectCriticEvaluator("passes")],
            new RunEvalsOptions<string>
            {
                JudgeConfig = new EvalJudgeConfig { OverrideAgent = judgeAgent },
            },
            experimentName: "agent-judge");

        judgeAgent.Configs.Should().ContainSingle();
        judgeAgent.Configs[0].UserMessage.Should().Contain("Evaluate whether the response satisfies");
        judgeAgent.Configs[0].UserMessage.Should().Contain("Response: response to hello");
    }

    [Fact]
    public async Task ExecuteAsync_JudgeOverrideAgent_ThrowingJudgeProducesDiagnostic()
    {
        var agent = new CapturingAgent();
        var judgeAgent = new CapturingAgent
        {
            FailuresBeforeSuccess = 1,
            FailureFactory = () => new InvalidOperationException("judge exploded"),
        };

        var report = await RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("hello"),
            [new AspectCriticEvaluator("passes")],
            new RunEvalsOptions<string>
            {
                JudgeConfig = new EvalJudgeConfig { OverrideAgent = judgeAgent },
            },
            experimentName: "agent-judge");

        report.Failures.Should().BeEmpty();
        report.Cases.Should().ContainSingle();
        report.Cases[0].EvaluatorFailures.Should().BeEmpty();
        var metric = report.Cases[0].EvaluationResult.Metrics[AspectCriticEvaluator.MetricName];
        metric.Diagnostics.Should().Contain(d => d.Message.Contains("judge exploded"));
    }

    [Fact]
    public async Task ExecuteAsync_DeterministicOnly_DoesNotRequireJudgeConfig()
    {
        var agent = new CapturingAgent();

        var report = await RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("hello"),
            [new OutputContainsEvaluator("response")],
            new RunEvalsOptions<string>(),
            experimentName: "deterministic-only");

        report.Failures.Should().BeEmpty();
        report.Cases.Should().ContainSingle();
        report.Cases[0].EvaluatorFailures.Should().BeEmpty();
        report.Cases[0].EvaluationResult.Metrics["Output Contains"]
            .Should().BeOfType<BooleanMetric>()
            .Which.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_CapturedAgentToolCall_DrivesToolEvaluatorsAndPersistsToolAttributes()
    {
        var toolCall = new ToolCallRecord(
            CallId: "call-read",
            Name: "ReadFile",
            ToolHarnessName: "FileSystem",
            ArgumentsJson: """{"path":"/tmp/a.txt"}""",
            Result: "contents",
            Duration: TimeSpan.FromMilliseconds(12),
            WasPermissionDenied: false);
        var agent = new CapturingAgent { ToolCalls = [toolCall] };
        var store = new InMemoryScoreStore();

        var report = await RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("read /tmp/a.txt"),
            [
                new ToolWasCalledEvaluator("ReadFile"),
                new ToolArgumentMatchesEvaluator("ReadFile", "path", "/tmp/a.txt"),
            ],
            new RunEvalsOptions<string>
            {
                PersistResults = true,
                ScoreStore = store,
            },
            experimentName: "nightly");

        report.Failures.Should().BeEmpty();
        report.Cases.Should().ContainSingle();
        report.Cases[0].EvaluatorFailures.Should().BeEmpty();
        report.Cases[0].EvaluationResult.Metrics["Tool Was Called"]
            .Should().BeOfType<BooleanMetric>()
            .Which.Value.Should().BeTrue();
        report.Cases[0].EvaluationResult.Metrics["Tool Argument Matches"]
            .Should().BeOfType<BooleanMetric>()
            .Which.Value.Should().BeTrue();

        var records = new List<ScoreRecord>();
        await foreach (var record in store.GetScoresAsync(sessionId: "nightly", threadId: "case-1"))
            records.Add(record);

        records.Should().HaveCount(2);
        records.Should().OnlyContain(r => r.Attributes != null);
        records.Select(r => r.Attributes).Should().AllBeEquivalentTo(records[0].Attributes);
        var attributes = records[0].Attributes;
        attributes.Should().NotBeNull();
        attributes!.Should().ContainKey("tool_calls")
            .WhoseValue.Should().BeAssignableTo<IEnumerable<ToolCallRecord>>()
            .Which.Should().ContainSingle()
            .Which.Should().Match<ToolCallRecord>(tc =>
                tc.CallId == "call-read" &&
                tc.Name == "ReadFile" &&
                tc.ArgumentsJson.Contains("/tmp/a.txt"));
    }

    [Fact]
    public async Task ExecuteAsync_EvaluatorConcurrency_RunsEvaluatorsInParallel()
    {
        var agent = new CapturingAgent();
        var probe = new EvaluatorConcurrencyProbe(expectedStarts: 2);
        var eval1 = new ProbeEvaluator("Probe 1", probe);
        var eval2 = new ProbeEvaluator("Probe 2", probe);

        var runTask = RunEvals.ExecuteAsync(
            agent,
            SingleCaseDataset("hello"),
            [eval1, eval2],
            new RunEvalsOptions<string> { EvaluatorConcurrency = 2 },
            experimentName: "evaluator-concurrency");

        await probe.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        probe.Release.SetResult();

        var report = await runTask;

        probe.MaxActive.Should().Be(2);
        report.Failures.Should().BeEmpty();
        report.Cases.Should().ContainSingle();
        report.Cases[0].EvaluatorFailures.Should().BeEmpty();
        report.Cases[0].EvaluationResult.Metrics.Should().ContainKey("Probe 1");
        report.Cases[0].EvaluationResult.Metrics.Should().ContainKey("Probe 2");
    }

    private static Dataset<string> SingleCaseDataset(string input) => new()
    {
        Cases =
        [
            new EvalCase<string>
            {
                Name = "case-1",
                Input = input,
            },
        ],
    };

    private sealed class CapturingAgent : IJudgeAgent
    {
        public const string ResponseModelId = "provider-reported-gpt-test";

        public List<AgentRunConfig> Configs { get; } = [];
        public int Attempts => _chatClient.Attempts;
        public int FailuresBeforeSuccess { get; init; }
        public Func<Exception>? FailureFactory { get; init; }
        public string? ResponseText { get; init; }
        public IReadOnlyList<ToolCallRecord> ToolCalls { get; init; } = [];

        private readonly CapturingChatClient _chatClient;
        private readonly HPD.Agent.Agent _agent;
        private int _judgeAttempts;

        public CapturingAgent()
        {
            _chatClient = new CapturingChatClient(this);
            var options = new ChatOptions
            {
                Tools =
                [
                    AIFunctionFactory.Create((string path) => "contents", "ReadFile"),
                ],
            };

            _agent = new HPD.Agent.Agent(
                new AgentConfig
                {
                    Name = nameof(CapturingAgent),
                    Clients = new AgentClientConfig { Chat = new ClientProviderConfig {
                        ProviderKey = "openai",
                        ModelName = "gpt-test",
                        DefaultChatOptions = options,
                    } },
                },
                _chatClient,
                options,
                middlewares: [new RecordingMiddleware(this)],
                providerRegistry: new CapturingProviderRegistry(_chatClient));
        }

        public static implicit operator HPD.Agent.Agent(CapturingAgent agent) => agent._agent;

        public Task<ChatResponse> RunAsync(AgentRunConfig config, CancellationToken ct = default)
        {
            Configs.Add(config);
            _judgeAttempts++;

            if (_judgeAttempts <= FailuresBeforeSuccess)
                throw FailureFactory?.Invoke() ?? new InvalidOperationException("failure");

            return Task.FromResult(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, ResponseText ?? $"response to {config.UserMessage}")]));
        }

        private sealed class RecordingMiddleware(CapturingAgent owner) : IAgentMiddleware
        {
            public Task BeforeMessageTurnAsync(
                BeforeMessageTurnContext context,
                CancellationToken cancellationToken)
            {
                owner.Configs.Add(context.RunConfig);
                return Task.CompletedTask;
            }
        }

        private sealed class CapturingChatClient(CapturingAgent owner) : IChatClient
        {
            private bool _toolCallEmitted;

            public int Attempts { get; private set; }
            public ChatClientMetadata Metadata => new("CapturingChatClient");

            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => Task.FromResult(new ChatResponse(
                    [new ChatMessage(ChatRole.Assistant, owner.ResponseText ?? $"response to {LastUserText(messages)}")]));

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                Attempts++;
                if (Attempts <= owner.FailuresBeforeSuccess)
                    throw owner.FailureFactory?.Invoke() ?? new InvalidOperationException("failure");

                await Task.Yield();

                if (owner.ToolCalls.Count > 0 && !_toolCallEmitted)
                {
                    _toolCallEmitted = true;
                    var toolCall = owner.ToolCalls[0];
                    yield return new ChatResponseUpdate
                    {
                        Contents =
                        [
                            new FunctionCallContent(
                                toolCall.CallId,
                                toolCall.Name,
                                JsonSerializer.Deserialize<Dictionary<string, object?>>(
                                    toolCall.ArgumentsJson) ?? new Dictionary<string, object?>()),
                        ],
                        FinishReason = ChatFinishReason.ToolCalls,
                    };
                    yield break;
                }

                yield return new ChatResponseUpdate
                {
                    Contents = [new TextContent(owner.ResponseText ?? $"response to {LastUserText(messages)}")],
                    ModelId = ResponseModelId,
                    FinishReason = ChatFinishReason.Stop,
                };
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }

            private static string LastUserText(IEnumerable<ChatMessage> messages)
                => messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        }

        private sealed class CapturingProviderRegistry(IChatClient client) : IProviderRegistry
        {
            public IProvider? GetProvider(string providerKey) =>
                new CapturingChatClientProvider(providerKey, client);

            public TProvider? GetProvider<TProvider>(string providerKey)
                where TProvider : class, IProvider
                => GetProvider(providerKey) as TProvider;

            public IReadOnlyCollection<string> GetRegisteredProviders() => ["openai", "test"];
            public void Register(IProvider provider) { }
            public bool IsRegistered(string providerKey) => true;
            public void Clear() { }
        }

        private sealed class CapturingChatClientProvider(string providerKey, IChatClient client) : IChatClientProvider
        {
            public string ProviderKey => providerKey;
            public string DisplayName => providerKey;
            public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null) => client;
            public HPD.Agent.ErrorHandling.IProviderErrorHandler CreateErrorHandler() => new StubErrorHandler();
            public ProviderMetadata GetMetadata() => new()
            {
                ProviderKey = providerKey,
                DisplayName = providerKey,
                Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
                {
                    [ProviderClientFamily.Chat] = new()
                    {
                        Family = ProviderClientFamily.Chat,
                        Capabilities = new Dictionary<string, object?>
                        {
                            ["SupportsStreaming"] = true,
                            ["SupportsFunctionCalling"] = true
                        }
                    }
                },
            };
            public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
                => ProviderValidationResult.Success();
        }
    }

    private sealed class EvaluatorConcurrencyProbe(int expectedStarts)
    {
        private int _active;
        private int _maxActive;
        private int _started;

        public TaskCompletionSource BothStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxActive => Volatile.Read(ref _maxActive);

        public async Task EnterAsync(CancellationToken ct)
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMax(active);

            if (Interlocked.Increment(ref _started) == expectedStarts)
                BothStarted.TrySetResult();

            await Release.Task.WaitAsync(ct);
        }

        public void Exit() => Interlocked.Decrement(ref _active);

        private void UpdateMax(int active)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxActive);
                if (active <= current)
                    return;

                if (Interlocked.CompareExchange(ref _maxActive, active, current) == current)
                    return;
            }
        }
    }

    private sealed class ProbeEvaluator(string metricName, EvaluatorConcurrencyProbe probe)
        : HpdDeterministicEvaluatorBase
    {
        public override IReadOnlyCollection<string> EvaluationMetricNames => [metricName];

        protected override async ValueTask<EvaluationResult> EvaluateDeterministicAsync(
            IEnumerable<ChatMessage> messages,
            ChatResponse modelResponse,
            IEnumerable<EvaluationContext>? additionalContext,
            CancellationToken cancellationToken)
        {
            try
            {
                await probe.EnterAsync(cancellationToken);
            }
            finally
            {
                probe.Exit();
            }

            return new EvaluationResult(new BooleanMetric(metricName) { Value = true });
        }
    }
}
