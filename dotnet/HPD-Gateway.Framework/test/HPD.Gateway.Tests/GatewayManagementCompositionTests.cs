using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Gateway.Management;
using HPD.Gateway;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Collections.Immutable;
using System.Text.Json;
using HPD.Gateway.Abstractions.Serialization;

namespace HPD.Gateway.Tests;

public sealed class GatewayManagementCompositionTests
{
    [Fact]
    public async Task Default_composition_is_truthfully_process_local()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGatewayManagement(options => options.ManagementAuthorityId = "authority-a");
        await using ServiceProvider provider = services.BuildServiceProvider();

        GatewayAuthorityCapabilitySnapshot snapshot = await provider
            .GetRequiredService<IGatewayAuthorityRuntime>()
            .InitializeAsync();

        snapshot.ProviderId.Should().Be("inmemory");
        snapshot.Durability.Should().Be(GatewayAuthorityDurability.ProcessLocal);
        snapshot.CollectionIds.Should().Equal(GatewayAuthoritySchema.CollectionIds);
        snapshot.BackupSupported.Should().BeFalse();
        snapshot.RestoreSupported.Should().BeFalse();
    }

    [Fact]
    public async Task Durable_profile_rejects_process_local_provider_before_commands()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGatewayManagement(options =>
        {
            options.ManagementAuthorityId = "authority-a";
            options.RequiredDurability = GatewayAuthorityDurability.RestartDurable;
            options.DesiredStateTokenKey = Enumerable.Repeat((byte)0x45, 32).ToArray();
        });
        await using ServiceProvider provider = services.BuildServiceProvider();

        Func<Task> initialize = async () => await provider
            .GetRequiredService<IGatewayAuthorityRuntime>()
            .InitializeAsync();

        await initialize.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*restart-durable*");
    }

    [Fact]
    public void Composition_rejects_invalid_public_bounds()
    {
        var services = new ServiceCollection();
        Action compose = () => services.AddHpdGatewayManagement(options =>
            options.MaximumTargets = 0);
        compose.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Target_provisioning_is_atomic_replayable_and_exclusive()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGateway(static builder => builder.AddCoreFamilies());
        services.AddHpdGatewayManagement(options => options.ManagementAuthorityId = "authority-a");
        await using ServiceProvider provider = services.BuildServiceProvider();
        var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
        var actor = new GatewayManagementActor("actor-a", "test", "manage");
        var first = new GatewayProvisionTargetCommand(
            "namespace-a", "node-a", "key-a", actor, "correlation-a", "epoch-a");

        GatewayManagementCommandResult accepted = await commands.ProvisionTargetAsync(first);
        GatewayManagementCommandResult duplicate = await commands.ProvisionTargetAsync(first);
        GatewayManagementCommandResult competing = await commands.ProvisionTargetAsync(first with
        {
            NamespaceId = "namespace-b",
            IdempotencyKey = "key-b",
            CorrelationId = "correlation-b",
            AuthorityEpoch = "epoch-b",
        });

        accepted.State.Should().Be(GatewayManagementCommandState.Accepted, accepted.Code);
        duplicate.State.Should().Be(GatewayManagementCommandState.Duplicate);
        competing.State.Should().Be(GatewayManagementCommandState.Conflict, competing.Code);
    }

    [Fact]
    public async Task Submit_accepts_through_the_authoritative_reader_and_commits_the_complete_graph()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGateway(static builder => builder.AddCoreFamilies());
        services.AddHpdGatewayManagement(options => options.ManagementAuthorityId = "authority-a");
        await using ServiceProvider provider = services.BuildServiceProvider();
        var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
        var actor = new GatewayManagementActor("actor-a", "test", "manage");
        await commands.ProvisionTargetAsync(new(
            "namespace-a", "node-a", "provision-a", actor, "correlation-a", "epoch-a"));
        var configuration = GatewayConfigurationTests.CreateValidConfiguration() with
        {
            Routes = [GatewayConfigurationTests.CreateValidConfiguration().Routes[0] with
            {
                Declarations = new HPD.Gateway.Abstractions.RouteDeclarations(),
            }],
        };
        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(
            configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
        var command = new GatewaySubmitCommand(
            "namespace-a", "node-a", "submit-a", actor, "correlation-b",
            "test", "source-a", null, ImmutableArray.Create(utf8), Activate: false);

        GatewayManagementCommandResult accepted = await commands.SubmitAsync(command);
        GatewayManagementCommandResult duplicate = await commands.SubmitAsync(command);
        GatewayManagedRecord<GatewayDesiredState>? desired = await provider
            .GetRequiredService<IGatewayManagementReader>().GetDesiredAsync("node-a");

        accepted.State.Should().Be(GatewayManagementCommandState.Accepted, accepted.Code);
        accepted.DesiredStateToken.Should().NotBeNullOrWhiteSpace();
        duplicate.State.Should().Be(GatewayManagementCommandState.Duplicate, duplicate.Code);
        duplicate.DesiredStateToken.Should().Be(accepted.DesiredStateToken);
        desired.Should().NotBeNull();
        desired!.Value.NamespaceId.Should().Be("namespace-a");
        desired.Value.RevisionId.Should().Be(accepted.OperationId);
    }

    [Fact]
    public async Task Sqlite_is_restart_durable_and_replays_the_exact_provisioning_receipt()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-gateway-{Guid.NewGuid():N}.db");
        try
        {
            var command = new GatewayProvisionTargetCommand(
                "namespace-a", "node-a", "key-a",
                new GatewayManagementActor("actor-a", "test", "manage"),
                "correlation-a", "epoch-a");
            await using (ServiceProvider first = SqliteProvider(database))
            {
                await InitializeSqlite(first);
                GatewayAuthorityCapabilitySnapshot capabilities = await first
                    .GetRequiredService<IGatewayAuthorityRuntime>().InitializeAsync();
                capabilities.Durability.Should().Be(GatewayAuthorityDurability.RestartDurable);
                (await first.GetRequiredService<IGatewayManagementCommandCoordinator>()
                    .ProvisionTargetAsync(command)).State.Should().Be(GatewayManagementCommandState.Accepted);
            }

            await using (ServiceProvider restarted = SqliteProvider(database))
            {
                await InitializeSqlite(restarted);
                GatewayManagementCommandResult replay = await restarted
                    .GetRequiredService<IGatewayManagementCommandCoordinator>()
                    .ProvisionTargetAsync(command);
                replay.State.Should().Be(GatewayManagementCommandState.Duplicate, replay.Code);
            }
        }
        finally
        {
            if (File.Exists(database)) File.Delete(database);
            if (File.Exists(database + "-wal")) File.Delete(database + "-wal");
            if (File.Exists(database + "-shm")) File.Delete(database + "-shm");
        }
    }

    private static ServiceProvider SqliteProvider(string database)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGateway(static builder => builder.AddCoreFamilies());
        services.AddHpdGatewayManagement(
            options =>
            {
                options.ManagementAuthorityId = "authority-a";
                options.RequiredDurability = GatewayAuthorityDurability.RestartDurable;
                options.DesiredStateTokenKey = Enumerable.Repeat((byte)0x34, 32).ToArray();
            },
            builder =>
            {
                builder.ConfigureSchema(schema => schema.PlanProtectionKey = Enumerable.Repeat((byte)0x12, 32).ToArray());
                builder.ConfigureTokenProtection(tokens => tokens.ActiveKey = new BaseOpaqueTokenKey
                {
                    Id = 1,
                    Key = Enumerable.Repeat((byte)0x23, 32).ToArray(),
                });
                builder.UseSqlite(sqlite =>
                {
                    sqlite.StoreId = "gateway-management";
                    sqlite.DataSource = database;
                    sqlite.AdministrationEnabled = true;
                    sqlite.AllowClientRequestedIds = true;
                });
            });
        return services.BuildServiceProvider();
    }

    private static async Task InitializeSqlite(ServiceProvider provider)
    {
        IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
        BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest
        {
            StoreId = "gateway-management",
        })).Value!;
        (await schemas.ApplyAsync(new BaseSchemaApplyRequest
        {
            ProtectedArtifact = plan.ProtectedArtifact,
        })).IsSuccess().Should().BeTrue();
    }
}
