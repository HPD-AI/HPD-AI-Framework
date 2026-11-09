// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HPD.Agent.Evaluations;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.HpdBase;
using HPD.Agent.Evaluations.Storage;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Evaluations.HpdBase.Tests;

public sealed class HpdBaseDatasetStoreTests
{
    [Fact]
    public async Task DatasetStore_RoundTripsHistoryDiffAndSurvivesRestart()
    {
        string database = TemporaryDatabase();
        DateTimeOffset january = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset february = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        try
        {
            await using (ServiceProvider first = await CreateProviderAsync(database))
            {
                IDatasetStore store = first.GetRequiredService<IDatasetStore>();
                await store.RegisterDatasetVersionAsync(
                    Dataset(
                        "v1",
                        [
                            Case("case-a", "old", "truth"),
                            Case("removed", "remove", "truth"),
                        ]),
                    new DatasetRegistrationOptions<string>
                    {
                        Description = "First revision",
                        RegisteredAt = january,
                        Metadata = new Dictionary<string, object>
                        {
                            ["owner"] = "tests",
                        },
                    });
                await store.RegisterDatasetVersionAsync(
                    Dataset(
                        "v2",
                        [
                            Case("case-a", "new", "truth"),
                            Case("added", "add", "truth"),
                        ]),
                    new DatasetRegistrationOptions<string>
                    {
                        Description = "Second revision",
                        RegisteredAt = february,
                    });

                DatasetRecord head = (await store.GetDatasetAsync("support"))!;
                head.CurrentVersion.Should().Be("v2");
                head.Description.Should().Be("Second revision");

                var versions = new List<DatasetVersionRecord>();
                await foreach (DatasetVersionRecord version in
                               store.GetDatasetVersionsAsync("support"))
                {
                    versions.Add(version);
                }
                versions.Select(static value => value.Version)
                    .Should().Equal("v1", "v2");

                var januaryCases = new List<EvalCase<string>>();
                await foreach (EvalCase<string> evalCase in
                               store.GetActiveCasesAsync<string>("support", january))
                {
                    januaryCases.Add(evalCase);
                }
                januaryCases.Should().HaveCount(2);
                januaryCases.Single(static value => value.CaseId == "case-a")
                    .Input.Should().Be("old");

                var history = new List<EvalCase<string>>();
                await foreach (EvalCase<string> evalCase in
                               store.GetCaseHistoryAsync<string>("support", "case-a"))
                {
                    history.Add(evalCase);
                }
                history.Should().HaveCount(2);
                history[0].Input.Should().Be("old");
                history[0].ValidTo.Should().Be(february);
                history[1].Input.Should().Be("new");

                DatasetVersionDiff<string> diff =
                    await store.CompareVersionsAsync<string>("support", "v1", "v2");
                diff.Added.Should().ContainSingle()
                    .Which.CaseId.Should().Be("added");
                diff.Removed.Should().ContainSingle()
                    .Which.CaseId.Should().Be("removed");
                diff.Changed.Should().ContainSingle()
                    .Which.CaseId.Should().Be("case-a");
            }

            await using ServiceProvider restarted = await CreateProviderAsync(database);
            IDatasetStore restartedStore = restarted.GetRequiredService<IDatasetStore>();
            Dataset<string>? loadedVersion = await restartedStore
                .GetDatasetVersionAsync<string>("support", "v2");
            loadedVersion.Should().NotBeNull();
            loadedVersion!.Cases.Single(static value => value.CaseId == "case-a")
                .Input.Should().Be("new");
            (await restartedStore.GetDatasetAsync("support"))!
                .CurrentVersion.Should().Be("v2");
        }
        finally
        {
            ClearDatabase(database);
        }
    }

    [Fact]
    public async Task DatasetStore_RejectsVersionContentChangesAndUnserializedEvaluators()
    {
        string database = TemporaryDatabase();
        try
        {
            await using ServiceProvider provider = await CreateProviderAsync(database);
            IDatasetStore store = provider.GetRequiredService<IDatasetStore>();
            await store.RegisterDatasetVersionAsync(
                Dataset("v1", [Case("case-a", "old", "truth")]));

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.RegisterDatasetVersionAsync(
                    Dataset("v1", [Case("case-a", "different", "truth")])));

            var evaluatorDataset = new Dataset<string>
            {
                DatasetId = "evaluator-dataset",
                Version = "v1",
                Cases = [Case("case-a", "input", "truth")],
                Evaluators = [Eval.OutputEquals("expected")],
            };
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.RegisterDatasetVersionAsync(evaluatorDataset));
        }
        finally
        {
            ClearDatabase(database);
        }
    }

    [Fact]
    public async Task DatasetStore_RoundTripsEvaluatorInstancesThroughConfiguredCodec()
    {
        string database = TemporaryDatabase();
        try
        {
            DatasetCodec<string> codec = new(
                DatasetStoreTestJsonContext.Default.String,
                serializeEvaluator: static _ => JsonValue.Create("output-equals"),
                deserializeEvaluator: static _ => Eval.OutputEquals("expected"));
            await using ServiceProvider provider =
                await CreateProviderAsync(database, codec);
            IDatasetStore store = provider.GetRequiredService<IDatasetStore>();
            var dataset = new Dataset<string>
            {
                DatasetId = "evaluator-roundtrip",
                Version = "v1",
                Cases = [Case("case-a", "input", "truth")],
                Evaluators = [Eval.OutputEquals("expected")],
            };

            await store.RegisterDatasetVersionAsync(dataset);
            Dataset<string> loaded = (await store.GetDatasetVersionAsync<string>(
                "evaluator-roundtrip", "v1"))!;
            loaded.Evaluators.Should().ContainSingle();
        }
        finally
        {
            ClearDatabase(database);
        }
    }

    private static Dataset<string> Dataset(
        string version,
        IReadOnlyList<EvalCase<string>> cases) => new()
        {
            DatasetId = "support",
            Version = version,
            Cases = cases,
        };

    private static EvalCase<string> Case(
        string id,
        string input,
        string truth) => new()
        {
            CaseId = id,
            Name = id,
            Input = input,
            GroundTruth = truth,
        };

    private static DatasetCodec<string> Codec() =>
        new(
            DatasetStoreTestJsonContext.Default.String,
            fingerprintCase: static evalCase =>
                evalCase.CaseId + "|" + evalCase.Input + "|" + evalCase.GroundTruth);

    private static async Task<ServiceProvider> CreateProviderAsync(
        string database,
        DatasetCodec<string>? codec = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder
            .AddHpdEvaluationStores()
            .ConfigureSchema(options =>
                options.PlanProtectionKey = Enumerable.Repeat((byte)0x74, 32).ToArray())
            .AddPolicyAuthority(
                new BasePolicyAuthorityDefinition
                {
                    Id = "hpd.agent.evaluations.dataset.tests.policy",
                    Version = 1,
                    OwningModuleId = "hpd.agent.evaluations",
                    EvaluatorContractId = "hpd.agent.evaluations.dataset.tests.policy-evaluator",
                    EvaluatorContractVersion = 1,
                    CompositionOrder = 0,
                },
                new AllowPolicyEvaluator())
            .UseStore(SqliteStore.Configure(options =>
            {
                options.StoreId = "sqlite";
                options.DataSource = database;
            })));
        services.AddHpdBaseDatasetStore(options => options.AddCodec(codec ?? Codec()));

        ServiceProvider provider = services.BuildServiceProvider();
        IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
        OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(
            new BaseSchemaPlanRequest { StoreId = "sqlite" });
        planned.IsSuccess().Should().BeTrue(planned.Error?.Code + ":" + planned.Error?.Message);
        OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(
            new BaseSchemaApplyRequest
            {
                ProtectedArtifact = planned.Value!.ProtectedArtifact,
            });
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
            "hpd-evaluation-base-dataset-store-" + Guid.NewGuid().ToString("N") + ".db");

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

[JsonSerializable(typeof(string))]
internal sealed partial class DatasetStoreTestJsonContext : JsonSerializerContext;
