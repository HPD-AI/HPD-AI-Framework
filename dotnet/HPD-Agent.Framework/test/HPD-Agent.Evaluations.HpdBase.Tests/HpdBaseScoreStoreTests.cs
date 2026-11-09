// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Evaluations.HpdBase;
using HPD.Agent.Evaluations.Storage;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Evaluations.HpdBase.Tests;

public sealed class HpdBaseScoreStoreTests
{
    [Fact]
    public async Task ScoreStore_PersistsQueriesAndReusesAnalytics()
    {
        string database = TemporaryDatabase();
        try
        {
            await using ServiceProvider provider = await CreateProviderAsync(database);
            IScoreStore store = provider.GetRequiredService<IScoreStore>();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            await store.WriteScoreAsync(Score("score-1", true, "s1", "main", 0.9, now));
            await store.WriteScoreAsync(Score("score-2", false, "s1", "fork", 0.2, now.AddMinutes(1)));

            var sessionScores = new List<ScoreRecord>();
            await foreach (ScoreRecord score in store.GetScoresAsync(sessionId: "s1"))
                sessionScores.Add(score);
            sessionScores.Should().HaveCount(2);

            (await store.GetPassRateAsync("Quality")).Should().Be(0.5);
            ScoreTrend trend = await store.GetTrendAsync(
                "Quality", now.AddMinutes(-1), now.AddMinutes(2), TimeSpan.FromMinutes(5));
            trend.Buckets.Should().ContainSingle().Which.Count.Should().Be(2);
        }
        finally
        {
            ClearDatabase(database);
        }
    }

    [Fact]
    public async Task ScoreStore_RunsRoundTripDeleteAndSurviveRestart()
    {
        string database = TemporaryDatabase();
        var score = Score("durable-score", true, "durable-session");
        var run = MakeRun("durable-run", "case-a", "1");
        try
        {
            await using (ServiceProvider first = await CreateProviderAsync(database))
            {
                IScoreStore store = first.GetRequiredService<IScoreStore>();
                await store.WriteScoreAsync(score);
                await store.WriteRunAsync(run);
            }

            await using ServiceProvider restarted = await CreateProviderAsync(database);
            IScoreStore restartedStore = restarted.GetRequiredService<IScoreStore>();
            var scores = new List<ScoreRecord>();
            await foreach (ScoreRecord item in restartedStore.GetScoresAsync(sessionId: "durable-session"))
                scores.Add(item);
            scores.Should().ContainSingle().Which.Result.Metrics.Should().ContainKey("Quality");

            var runs = new List<EvaluationRunRecord>();
            await foreach (EvaluationRunRecord item in restartedStore.GetRunsAsync("durable-run"))
                runs.Add(item);
            runs.Should().ContainSingle().Which.ScenarioName.Should().Be("case-a");

            await restartedStore.DeleteRunsAsync("durable-run");
            runs.Clear();
            await foreach (EvaluationRunRecord item in restartedStore.GetRunsAsync("durable-run"))
                runs.Add(item);
            runs.Should().BeEmpty();
        }
        finally
        {
            ClearDatabase(database);
        }
    }

    private static ScoreRecord Score(
        string id,
        bool passed,
        string sessionId,
        string threadId = "main",
        double value = 1.0,
        DateTimeOffset? createdAt = null) => new()
        {
            Id = id,
            EvaluatorName = "Quality",
            EvaluatorVersion = "1.0.0",
            Result = new EvaluationResult(
                new BooleanMetric("Quality Passed")
                {
                    Value = passed,
                    Interpretation = new EvaluationMetricInterpretation(
                        passed ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                        failed: !passed),
                },
                new NumericMetric("Quality")
                {
                    Value = value,
                    Interpretation = new EvaluationMetricInterpretation(
                        passed ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                        failed: !passed),
                }),
            Source = EvaluationSource.Test,
            SessionId = sessionId,
            ThreadId = threadId,
            TurnIndex = 0,
            AgentName = "test-agent",
            Policy = EvalPolicy.MustAlwaysPass,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };

    private static EvaluationRunRecord MakeRun(
        string executionName,
        string scenarioName,
        string iterationName) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ExecutionName = executionName,
            ScenarioName = scenarioName,
            IterationName = iterationName,
            CreatedAt = DateTimeOffset.UtcNow,
            Messages = [new ChatMessage(ChatRole.User, "hello")],
            ModelResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, "world")]),
            EvaluationResult = new EvaluationResult(new BooleanMetric("Passed") { Value = true }),
            Source = EvaluationSource.Test,
            AgentName = "test-agent",
            SessionId = executionName,
            ThreadId = scenarioName,
            TurnIndex = 0,
        };

    private static async Task<ServiceProvider> CreateProviderAsync(string database)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder
            .AddHpdEvaluationStores()
            .ConfigureSchema(options =>
                options.PlanProtectionKey = Enumerable.Repeat((byte)0x72, 32).ToArray())
            .AddPolicyAuthority(
                new BasePolicyAuthorityDefinition
                {
                    Id = "hpd.agent.evaluations.tests.policy",
                    Version = 1,
                    OwningModuleId = "hpd.agent.evaluations",
                    EvaluatorContractId = "hpd.agent.evaluations.tests.policy-evaluator",
                    EvaluatorContractVersion = 1,
                    CompositionOrder = 0,
                },
                new AllowPolicyEvaluator())
            .UseStore(SqliteStore.Configure(options =>
            {
                options.StoreId = "sqlite";
                options.DataSource = database;
            })));
        services.AddHpdBaseEvaluationStore();

        ServiceProvider provider = services.BuildServiceProvider();
        IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
        OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(
            new BaseSchemaPlanRequest { StoreId = "sqlite" });
        planned.IsSuccess().Should().BeTrue(planned.Error?.Code + ":" + planned.Error?.Message);
        OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(
            new BaseSchemaApplyRequest { ProtectedArtifact = planned.Value!.ProtectedArtifact });
        applied.IsSuccess().Should().BeTrue(applied.Error?.Code + ":" + applied.Error?.Message);
        OperationResult<BaseApplicationReadiness> initialized =
            await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
        initialized.IsSuccess().Should().BeTrue(
            initialized.Error?.Code + ":" + initialized.Error?.Message);
        return provider;
    }

    private static string TemporaryDatabase() =>
        Path.Combine(
            Path.GetTempPath(),
            "hpd-evaluation-base-store-" + Guid.NewGuid().ToString("N") + ".db");

    private static void ClearDatabase(string database)
    {
        SqliteConnection.ClearAllPools();
        foreach (string candidate in new[]
                 {
                     database,
                     database + "-wal",
                     database + "-shm",
                 })
        {
            if (File.Exists(candidate))
                File.Delete(candidate);
        }
    }

    private sealed class AllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed,
            });
        }
    }
}
