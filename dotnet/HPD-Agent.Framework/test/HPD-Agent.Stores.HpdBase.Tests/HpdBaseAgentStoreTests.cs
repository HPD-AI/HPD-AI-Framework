// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent;
using HPD.Agent.Stores.HpdBase;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace HPD.Agent.Stores.HpdBase.Tests;

public sealed class HpdBaseAgentStoreTests
{
    [Fact]
    public async Task AgentStore_RoundTripsListsAndDeletesThroughSqlite()
    {
        string database = TemporaryDatabase();
        try
        {
            await using ServiceProvider first = await CreateProviderAsync(database);
            IAgentStore store = first.GetRequiredService<IAgentStore>();
            StoredAgent agent = MakeAgent("agent-a");

            await store.SaveAsync(agent);

            AssertAgent(await store.LoadAsync(agent.Id), agent);
            (await store.ListIdsAsync()).Should().Equal(agent.Id);

            await store.DeleteAsync(agent.Id);
            (await store.LoadAsync(agent.Id)).Should().BeNull();
        }
        finally
        {
            ClearDatabase(database);
        }
    }

    [Fact]
    public async Task AgentStore_SurvivesProviderRestart()
    {
        string database = TemporaryDatabase();
        try
        {
            StoredAgent agent = MakeAgent("agent-durable");

            await using (ServiceProvider first = await CreateProviderAsync(database))
            {
                await first.GetRequiredService<IAgentStore>().SaveAsync(agent);
            }

            await using ServiceProvider restarted = await CreateProviderAsync(database);
            IAgentStore store = restarted.GetRequiredService<IAgentStore>();

            AssertAgent(await store.LoadAsync(agent.Id), agent);
            store.PersistenceCapability.Should().Be(StorePersistenceCapability.RestartDurable);
        }
        finally
        {
            ClearDatabase(database);
        }
    }

    private static StoredAgent MakeAgent(string id) => new()
    {
        Id = id,
        Name = "Durable agent",
        Config = new AgentConfig
        {
            Name = "Durable agent",
            SystemInstructions = "Persist this definition.",
        },
        CreatedAt = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 11, 9, 12, 5, 0, DateTimeKind.Utc),
        Metadata = new Dictionary<string, object>
        {
            ["owner"] = "tests",
        },
    };

    private static void AssertAgent(StoredAgent? actual, StoredAgent expected)
    {
        actual.Should().NotBeNull();
        actual!.Id.Should().Be(expected.Id);
        actual.Name.Should().Be(expected.Name);
        actual.Config.Name.Should().Be(expected.Config.Name);
        actual.Config.SystemInstructions.Should().Be(expected.Config.SystemInstructions);
        actual.CreatedAt.Should().Be(expected.CreatedAt);
        actual.UpdatedAt.Should().Be(expected.UpdatedAt);
        actual.Metadata.Should().ContainKey("owner");
        actual.Metadata!["owner"].Should().BeOfType<JsonElement>()
            .Which.GetString().Should().Be("tests");
    }

    private static async Task<ServiceProvider> CreateProviderAsync(string database)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder
            .AddHpdAgentStores()
            .ConfigureSchema(options =>
                options.PlanProtectionKey = Enumerable.Repeat((byte)0x71, 32).ToArray())
            .AddPolicyAuthority(
                new BasePolicyAuthorityDefinition
                {
                    Id = "hpd.agent.stores.tests.policy",
                    Version = 1,
                    OwningModuleId = "hpd.agent.stores",
                    EvaluatorContractId = "hpd.agent.stores.tests.policy-evaluator",
                    EvaluatorContractVersion = 1,
                    CompositionOrder = 0,
                },
                new AllowPolicyEvaluator())
            .UseStore(SqliteStore.Configure(options =>
            {
                options.StoreId = "sqlite";
                options.DataSource = database;
            })));
        services.AddHpdBaseAgentStore();

        ServiceProvider provider = services.BuildServiceProvider();
        IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
        OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(
            new BaseSchemaPlanRequest { StoreId = "sqlite" });
        planned.IsSuccess().Should().BeTrue(planned.Error?.Code + ":" + planned.Error?.Message);
        BaseSchemaPlan plan = planned.Value!;

        OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(
            new BaseSchemaApplyRequest
            {
                ProtectedArtifact = plan.ProtectedArtifact,
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
            "hpd-agent-base-store-" + Guid.NewGuid().ToString("N") + ".db");

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
