using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Gateway.Management;
using HPD.Gateway;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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

        Action attempts = () => new ServiceCollection().AddHpdGatewayManagement(options =>
            options.MaximumDeliveryAttempts = 0);
        Action lease = () => new ServiceCollection().AddHpdGatewayManagement(options =>
            options.DeliveryClaimLease = TimeSpan.FromMilliseconds(999));
        attempts.Should().Throw<ArgumentOutOfRangeException>();
        lease.Should().Throw<ArgumentOutOfRangeException>();
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
    public async Task Local_target_provisioning_reserves_one_namespace_neutral_epoch_before_acceptance()
    {
        await using ServiceProvider provider = InMemoryProvider();
        var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
        var actor = new GatewayManagementActor("actor-a", "test", "manage");
        var command = new GatewayLocalProvisionTargetCommand(
            "namespace-a", "node-reserved", "key-a", actor, "correlation-a");

        GatewayManagementCommandResult accepted = await commands.ProvisionLocalTargetAsync(command);
        GatewayManagementCommandResult duplicate = await commands.ProvisionLocalTargetAsync(command);
        GatewayManagementCommandResult competing = await commands.ProvisionLocalTargetAsync(command with
        {
            NamespaceId = "namespace-b",
            IdempotencyKey = "key-b",
            CorrelationId = "correlation-b",
        });
        BaseRecord<GatewayTargetEpochReservation>[] reservations = (await TrustedSession(provider)
            .Collection(GatewayTargetEpochReservation.Collection).Query()
            .Take(2).ToArrayAsync(2)).RequireValue();
        BaseRecord<GatewayTargetEpochReservationReceipt>[] receipts = (await TrustedSession(provider)
            .Collection(GatewayTargetEpochReservationReceipt.Collection).Query()
            .Take(2).ToArrayAsync(2)).RequireValue();
        BaseRecord<GatewayNodeDeliveryAuthorityState>[] delivery = (await TrustedSession(provider)
            .Collection(GatewayNodeDeliveryAuthorityState.Collection).Query()
            .Take(2).ToArrayAsync(2)).RequireValue();

        accepted.State.Should().Be(GatewayManagementCommandState.Accepted, accepted.Code);
        duplicate.State.Should().Be(GatewayManagementCommandState.Duplicate, duplicate.Code);
        competing.State.Should().Be(GatewayManagementCommandState.Conflict, competing.Code);
        reservations.Should().ContainSingle();
        receipts.Should().ContainSingle();
        delivery.Should().ContainSingle();
        reservations[0].Value.AuthorityEpoch.Should().Be(delivery[0].Value.AuthorityEpoch);
        reservations[0].Value.AuthorityEpoch.Should().HaveLength(32);
        reservations[0].Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Epoch_reservation_reconstructs_the_same_attempt_after_store_absence()
    {
        string firstDatabase = Path.Combine(Path.GetTempPath(), $"hpd-gateway-epoch-a-{Guid.NewGuid():N}.db");
        string secondDatabase = Path.Combine(Path.GetTempPath(), $"hpd-gateway-epoch-b-{Guid.NewGuid():N}.db");
        try
        {
            string? firstEpoch = null;
            foreach (string database in new[] { firstDatabase, secondDatabase })
            {
                await using ServiceProvider provider = SqliteProvider(database);
                await InitializeSqlite(provider);
                GatewayManagementCommandResult result = await provider
                    .GetRequiredService<IGatewayManagementCommandCoordinator>()
                    .ProvisionLocalTargetAsync(new("namespace-a", "node-recovered", "provision-a",
                        new("actor-a", "test", "manage"), "correlation-a"));
                result.IsAccepted.Should().BeTrue(result.Code);
                GatewayTargetEpochReservation reservation = (await TrustedSession(provider)
                    .Collection(GatewayTargetEpochReservation.Collection).Query()
                    .Take(1).ToArrayAsync(1)).RequireValue().Single().Value;
                if (firstEpoch is null) firstEpoch = reservation.AuthorityEpoch;
                else reservation.AuthorityEpoch.Should().Be(firstEpoch);
            }
        }
        finally
        {
            foreach (string path in new[] { firstDatabase, secondDatabase })
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
                if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
            }
        }
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
        accepted.DesiredStateToken.Should().BeNull();
        duplicate.State.Should().Be(GatewayManagementCommandState.Duplicate, duplicate.Code);
        duplicate.DesiredStateToken.Should().BeNull();
        desired.Should().BeNull("submit-only must not change desired state");
    }

    [Fact]
    public async Task Older_duplicate_returns_its_receipt_owned_token_after_desired_advances()
    {
        await using ServiceProvider provider = InMemoryProvider();
        var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
        var actor = new GatewayManagementActor("actor-a", "test", "manage");
        await commands.ProvisionTargetAsync(new(
            "namespace-a", "node-a", "provision-a", actor, "correlation-a", "epoch-a"));
        ImmutableArray<byte> configuration = ConfigurationBytes();
        var firstCommand = new GatewaySubmitCommand(
            "namespace-a", "node-a", "submit-a", actor, "correlation-b",
            "test", "source-a", "first", configuration, Activate: true);
        GatewayManagementCommandResult first = await commands.SubmitAsync(firstCommand);
        var secondCommand = firstCommand with
        {
            IdempotencyKey = "submit-b",
            CorrelationId = "correlation-c",
            Description = "second",
            ExpectedDesiredStateToken = first.DesiredStateToken,
        };
        GatewayManagementCommandResult second = await commands.SubmitAsync(secondCommand);
        GatewayManagementCommandResult replay = await commands.SubmitAsync(firstCommand);

        second.State.Should().Be(GatewayManagementCommandState.Accepted, second.Code);
        second.DesiredStateToken.Should().NotBe(first.DesiredStateToken);
        replay.State.Should().Be(GatewayManagementCommandState.Duplicate, replay.Code);
        replay.DesiredStateToken.Should().Be(first.DesiredStateToken);
    }

    [Fact]
    public async Task Hosted_reconciler_delivers_new_work_when_terminal_history_fills_the_bound()
    {
        var activator = new RecordingNodeActivator();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGateway(static builder => builder.AddCoreFamilies());
        services.AddHpdGatewayManagement(options =>
        {
            options.ManagementAuthorityId = "authority-a";
            options.MaximumTargets = 1;
            options.ReconciliationInterval = TimeSpan.FromMilliseconds(100);
        });
        services.Replace(ServiceDescriptor.Singleton<IGatewayNodeActivator>(activator));
        await using ServiceProvider provider = services.BuildServiceProvider();
        var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
        var actor = new GatewayManagementActor("actor-a", "test", "manage");
        await commands.ProvisionTargetAsync(new(
            "namespace-a", "node-a", "provision-a", actor, "correlation-a", "epoch-a"));
        ImmutableArray<byte> configuration = ConfigurationBytes();
        var firstCommand = new GatewaySubmitCommand(
            "namespace-a", "node-a", "submit-a", actor, "correlation-b",
            "test", "source-a", "first", configuration, Activate: true);

        IHostedService worker = provider.GetRequiredService<GatewayManagementReconciliationWorker>();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            GatewayManagementCommandResult first = await commands.SubmitAsync(firstCommand);
            await WaitUntil(() => activator.Count >= 1);
            GatewayManagementCommandResult second = await commands.SubmitAsync(firstCommand with
            {
                IdempotencyKey = "submit-b",
                CorrelationId = "correlation-c",
                Description = "second",
                ExpectedDesiredStateToken = first.DesiredStateToken,
            });
            second.State.Should().Be(GatewayManagementCommandState.Accepted, second.Code);
            await WaitUntil(() => activator.Count >= 2);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Administrative_purge_replay_does_not_execute_the_provider_twice()
    {
        await WithSqlite(async provider =>
        {
            var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
            var actor = new GatewayManagementActor("actor-a", "test", "manage");
            await commands.ProvisionTargetAsync(new(
                "namespace-a", "node-a", "provision-a", actor, "correlation-a", "epoch-a"));
            BaseRecord<GatewayAdministrativeAuditRecord> audit = (await TrustedSession(provider)
                .Collection(GatewayAdministrativeAuditRecord.Collection).Query()
                .Where(GatewayAdministrativeAuditRecord.Fields.Operation, "provision-target")
                .Take(1).ToArrayAsync(1)).RequireValue().Single();
            var administration = provider.GetRequiredService<IGatewayManagementAdministration>();

            GatewayAdministrativeResult first = await administration.PurgeAsync(
                "namespace-a", "purge-a", actor, GatewayAuthoritySchema.AdministrativeAudit,
                [audit.Id.Value], null);
            GatewayAdministrativeResult replay = await administration.PurgeAsync(
                "namespace-a", "purge-a", actor, GatewayAuthoritySchema.AdministrativeAudit,
                [audit.Id.Value], null);

            first.State.Should().Be(GatewayAdministrativeCompletionState.Completed, first.Code);
            replay.Should().Be(first);
        });
    }

    [Fact]
    public async Task Administrative_replay_binds_complete_actor_attribution()
    {
        await WithSqlite(async provider =>
        {
            var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
            var actor = new GatewayManagementActor("actor-a", "test", "manage");
            await commands.ProvisionTargetAsync(new(
                "namespace-a", "node-a", "provision-a", actor, "correlation-a", "epoch-a"));
            BaseRecord<GatewayAdministrativeAuditRecord> audit = (await TrustedSession(provider)
                .Collection(GatewayAdministrativeAuditRecord.Collection).Query()
                .Where(GatewayAdministrativeAuditRecord.Fields.Operation, "provision-target")
                .Take(1).ToArrayAsync(1)).RequireValue().Single();
            var administration = provider.GetRequiredService<IGatewayManagementAdministration>();
            (await administration.PurgeAsync(
                "namespace-a", "purge-a", actor, GatewayAuthoritySchema.AdministrativeAudit,
                [audit.Id.Value], null)).State.Should().Be(GatewayAdministrativeCompletionState.Completed);

            await administration.Invoking(value => value.PurgeAsync(
                    "namespace-a", "purge-a", actor with { AuthorizationPolicy = "other-policy" },
                    GatewayAuthoritySchema.AdministrativeAudit, [audit.Id.Value], null).AsTask())
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*different semantics*");
        });
    }

    [Fact]
    public async Task Confirmed_administrative_failure_reconciles_to_terminal_failed()
    {
        await using ServiceProvider provider = InMemoryProvider();
        await provider.GetRequiredService<IGatewayAuthorityRuntime>().InitializeAsync();
        BaseSession session = TrustedSession(provider);
        var intentId = RecordId.Create("gwm.admin.intent.failed-test");
        var observationId = RecordId.Create("gwm.admin.observation.failed-test");
        (await session.Collection(GatewayAdministrativeOperationIntent.Collection).CreateAsync(
            intentId, new GatewayAdministrativeOperationIntent
            {
                NamespaceId = "namespace-a",
                Operation = GatewayAdministrativeOperationKind.Backup,
                ActorId = "actor-a",
                AuthenticationScheme = "test",
                AuthorizationPolicy = "manage",
                SubjectDigest = "failed-test",
            })).RequireValue();
        (await session.Collection(GatewayAdministrativeOperationObservation.Collection).CreateAsync(
            observationId, new GatewayAdministrativeOperationObservation
            {
                IntentId = intentId.Value,
                Kind = GatewayAdministrativeObservationKind.Failed,
                ResultCode = "base.admin.rejected",
                ResultJson = "{}"u8.ToArray(),
            })).RequireValue();

        (await provider.GetRequiredService<IGatewayManagementAdministration>()
            .ReconcilePendingAsync()).Should().Be(1);

        BaseRecord<GatewayAdministrativeOperationCompletion> completion = (await session
            .Collection(GatewayAdministrativeOperationCompletion.Collection).Query()
            .Take(1).ToArrayAsync(1)).RequireValue().Single();
        completion.Value.State.Should().Be(GatewayAdministrativeCompletionState.Failed);
    }

    [Fact]
    public async Task Sqlite_restart_recovers_committed_purge_without_an_observation()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-gateway-{Guid.NewGuid():N}.db");
        const string intentKey = "crash-window-purge";
        RecordId intentId = GatewayAuthorityRecordIds.CommandFact(
            "admin-intent", "namespace-a", "purge", intentKey, "v1");
        try
        {
            await using (ServiceProvider first = SqliteProvider(database))
            {
                await InitializeSqlite(first);
                var commands = first.GetRequiredService<IGatewayManagementCommandCoordinator>();
                var actor = new GatewayManagementActor("actor-a", "test", "manage");
                await commands.ProvisionTargetAsync(new(
                    "namespace-a", "node-a", "provision-a", actor, "correlation-a", "epoch-a"));
                BaseSession session = TrustedSession(first);
                BaseRecord<GatewayAdministrativeAuditRecord> audit = (await session
                    .Collection(GatewayAdministrativeAuditRecord.Collection).Query()
                    .Where(GatewayAdministrativeAuditRecord.Fields.Operation, "provision-target")
                    .Take(1).ToArrayAsync(1)).RequireValue().Single();
                string[] ids = [audit.Id.Value];
                await session.Collection(GatewayAdministrativeOperationIntent.Collection).CreateAsync(
                    intentId, new GatewayAdministrativeOperationIntent
                    {
                        NamespaceId = "namespace-a",
                        Operation = GatewayAdministrativeOperationKind.Purge,
                        ActorId = actor.ActorId,
                        AuthenticationScheme = actor.AuthenticationScheme,
                        AuthorizationPolicy = actor.AuthorizationPolicy,
                        SubjectDigest = "crash-window",
                        PurgeCollectionId = GatewayAuthoritySchema.AdministrativeAudit,
                        PurgeRecordIdsJson = JsonSerializer.SerializeToUtf8Bytes(
                            ids, GatewayManagementJsonContext.Default.StringArray),
                    }).AsTask();
                await session.Collection(GatewayPurgeAuthorityState.Collection).CreateAsync(
                    GatewayAuthorityRecordIds.PurgeAuthority("authority-a", GatewayAuthoritySchema.AdministrativeAudit),
                    new GatewayPurgeAuthorityState
                    {
                        ManagementAuthorityId = "authority-a",
                        CollectionId = GatewayAuthoritySchema.AdministrativeAudit,
                        ConfirmedGeneration = 0,
                        PendingIntentId = intentId.Value,
                    }).AsTask();
                BaseResult<BasePurgeResult> purged = await first.GetRequiredService<IHPDBaseAdministration>()
                    .PurgeAsync(new BasePurgeRequest
                    {
                        CollectionId = GatewayAuthoritySchema.AdministrativeAudit,
                        RecordIds = [audit.Id],
                        Principal = TrustedPrincipal(),
                        ReasonCode = "gateway.retention",
                        AuditReference = intentId.Value,
                        EvaluatedAt = DateTimeOffset.UtcNow,
                        ExpectedPurgeGeneration = 0,
                    });
                purged.RequireValue().PurgeGeneration.Should().Be(1);
            }

            await using (ServiceProvider restarted = SqliteProvider(database))
            {
                await InitializeSqlite(restarted);
                (await restarted.GetRequiredService<IGatewayManagementAdministration>()
                    .ReconcilePendingAsync()).Should().BeGreaterThan(0);
                BaseSession session = TrustedSession(restarted);
                BaseRecord<GatewayAdministrativeOperationObservation> observation = (await session
                    .Collection(GatewayAdministrativeOperationObservation.Collection).Query()
                    .Take(1).ToArrayAsync(1)).RequireValue().Single();
                observation.Value.IntentId.Should().Be(intentId.Value);
                observation.Value.Kind.Should().Be(GatewayAdministrativeObservationKind.Succeeded);
                observation.Value.ProviderGeneration.Should().Be(1);
                BaseRecord<GatewayPurgeAuthorityState> fence = (await session
                    .Collection(GatewayPurgeAuthorityState.Collection)
                    .GetAsync(GatewayAuthorityRecordIds.PurgeAuthority(
                        "authority-a", GatewayAuthoritySchema.AdministrativeAudit))).RequireValue();
                fence.Value.ConfirmedGeneration.Should().Be(1);
                fence.Value.PendingIntentId.Should().BeNull();
            }
        }
        finally
        {
            if (File.Exists(database)) File.Delete(database);
            if (File.Exists(database + "-wal")) File.Delete(database + "-wal");
            if (File.Exists(database + "-shm")) File.Delete(database + "-shm");
        }
    }

    [Fact]
    public async Task Restarted_sqlite_worker_scans_and_delivers_committed_outbox_work()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-gateway-{Guid.NewGuid():N}.db");
        var activator = new RecordingNodeActivator();
        try
        {
            await using (ServiceProvider first = SqliteProvider(database, activator))
            {
                await InitializeSqlite(first);
                var commands = first.GetRequiredService<IGatewayManagementCommandCoordinator>();
                var actor = new GatewayManagementActor("actor-a", "test", "manage");
                await commands.ProvisionTargetAsync(new(
                    "namespace-a", "node-a", "provision-a", actor, "correlation-a", "epoch-a"));
                GatewayManagementCommandResult accepted = await commands.SubmitAsync(new(
                    "namespace-a", "node-a", "submit-a", actor, "correlation-b",
                    "test", "source-a", null, ConfigurationBytes(), Activate: true));
                accepted.State.Should().Be(GatewayManagementCommandState.Accepted, accepted.Code);
                activator.Count.Should().Be(0);
            }

            await using (ServiceProvider restarted = SqliteProvider(database, activator))
            {
                await InitializeSqlite(restarted);
                IHostedService worker = restarted.GetRequiredService<GatewayManagementReconciliationWorker>();
                await worker.StartAsync(CancellationToken.None);
                try { await WaitUntil(() => activator.Count == 1); }
                finally { await worker.StopAsync(CancellationToken.None); }
            }
        }
        finally
        {
            if (File.Exists(database)) File.Delete(database);
            if (File.Exists(database + "-wal")) File.Delete(database + "-wal");
            if (File.Exists(database + "-shm")) File.Delete(database + "-shm");
        }
    }

    [Fact]
    public async Task Concurrent_reconciliation_claims_each_outbox_item_once()
    {
        var activator = new RecordingNodeActivator();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGateway(static builder => builder.AddCoreFamilies());
        services.AddHpdGatewayManagement(options => options.ManagementAuthorityId = "authority-a");
        services.Replace(ServiceDescriptor.Singleton<IGatewayNodeActivator>(activator));
        await using ServiceProvider provider = services.BuildServiceProvider();
        var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
        var actor = new GatewayManagementActor("actor-a", "test", "manage");
        await commands.ProvisionTargetAsync(new(
            "namespace-a", "node-a", "provision-a", actor, "correlation-a", "epoch-a"));
        GatewayManagementCommandResult accepted = await commands.SubmitAsync(new(
            "namespace-a", "node-a", "submit-a", actor, "correlation-b",
            "test", "source-a", null, ConfigurationBytes(), Activate: true));
        accepted.State.Should().Be(GatewayManagementCommandState.Accepted, accepted.Code);
        var delivery = provider.GetRequiredService<IGatewayDeliveryCoordinator>();

        await Task.WhenAll(
            delivery.ReconcileOnceAsync().AsTask(),
            delivery.ReconcileOnceAsync().AsTask());

        activator.Count.Should().Be(1);
    }

    [Fact]
    public async Task Delivery_attempt_limit_terminalizes_without_another_node_call()
    {
        var activator = new RecordingNodeActivator();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGateway(static builder => builder.AddCoreFamilies());
        services.AddHpdGatewayManagement(options =>
        {
            options.ManagementAuthorityId = "authority-a";
            options.MaximumDeliveryAttempts = 1;
        });
        services.Replace(ServiceDescriptor.Singleton<IGatewayNodeActivator>(activator));
        await using ServiceProvider provider = services.BuildServiceProvider();
        var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
        var actor = new GatewayManagementActor("actor-a", "test", "manage");
        await commands.ProvisionTargetAsync(new(
            "namespace-a", "node-a", "provision-a", actor, "correlation-a", "epoch-a"));
        (await commands.SubmitAsync(new(
            "namespace-a", "node-a", "submit-a", actor, "correlation-b",
            "test", "source-a", null, ConfigurationBytes(), Activate: true)))
            .State.Should().Be(GatewayManagementCommandState.Accepted);
        BaseSession session = TrustedSession(provider);
        BaseRecord<GatewayDeliveryOutboxItem> item = (await session
            .Collection(GatewayDeliveryOutboxItem.Collection).Query()
            .Take(1).ToArrayAsync(1)).RequireValue().Single();
        (await session.Collection(GatewayDeliveryOutboxItem.Collection).ReplaceAsync(
            item.Id, item.Value with { AttemptCount = 1 }, item.Revision)).RequireValue();

        GatewayDeliveryRunResult run = await provider.GetRequiredService<IGatewayDeliveryCoordinator>()
            .ReconcileOnceAsync();

        run.Failed.Should().Be(1);
        activator.Count.Should().Be(0);
        BaseRecord<GatewayNodeActivationOutcome> outcome = (await session
            .Collection(GatewayNodeActivationOutcome.Collection).Query()
            .Take(1).ToArrayAsync(1)).RequireValue().Single();
        outcome.Value.Code.Should().Be("management.delivery.attempt-limit");
    }

    [Fact]
    public async Task Fair_state_budget_serves_immediate_work_while_outcome_pending_remains()
    {
        var activator = new RecordingNodeActivator();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGateway(static builder => builder.AddCoreFamilies());
        services.AddHpdGatewayManagement(options =>
        {
            options.ManagementAuthorityId = "authority-a";
            options.MaximumTargets = 1;
        });
        services.Replace(ServiceDescriptor.Singleton<IGatewayNodeActivator>(activator));
        await using ServiceProvider provider = services.BuildServiceProvider();
        var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
        var actor = new GatewayManagementActor("actor-a", "test", "manage");
        await commands.ProvisionTargetAsync(new(
            "namespace-a", "node-a", "provision-a", actor, "correlation-a", "epoch-a"));
        var firstCommand = new GatewaySubmitCommand(
            "namespace-a", "node-a", "submit-a", actor, "correlation-b",
            "test", "source-a", "first", ConfigurationBytes(), Activate: true);
        GatewayManagementCommandResult first = await commands.SubmitAsync(firstCommand);
        (await commands.SubmitAsync(firstCommand with
        {
            IdempotencyKey = "submit-b",
            CorrelationId = "correlation-c",
            Description = "second",
            ExpectedDesiredStateToken = first.DesiredStateToken,
        })).State.Should().Be(GatewayManagementCommandState.Accepted);
        BaseSession session = TrustedSession(provider);
        BaseRecord<GatewayDeliveryOutboxItem>[] items = (await session
            .Collection(GatewayDeliveryOutboxItem.Collection).Query()
            .Take(2).ToArrayAsync(2)).RequireValue();
        BaseRecord<GatewayDeliveryOutboxItem> pending = items.OrderBy(value => value.Id.Value, StringComparer.Ordinal).First();
        pending = (await session.Collection(GatewayDeliveryOutboxItem.Collection).ReplaceAsync(
            pending.Id, pending.Value with
            {
                State = GatewayDeliveryState.OutcomePersistencePending,
                PendingOutcomeKind = GatewayNodeOutcomeKind.RejectedBeforePublish,
                PendingOutcomeCode = "test.pending",
            }, pending.Revision)).RequireValue();
        var delivery = provider.GetRequiredService<IGatewayDeliveryCoordinator>();

        await delivery.ReconcileOnceAsync();
        BaseRecord<GatewayDeliveryOutboxItem> terminal = (await session
            .Collection(GatewayDeliveryOutboxItem.Collection).GetAsync(pending.Id)).RequireValue();
        await session.Collection(GatewayDeliveryOutboxItem.Collection).ReplaceAsync(
            terminal.Id, terminal.Value with
            {
                State = GatewayDeliveryState.OutcomePersistencePending,
                PendingOutcomeKind = GatewayNodeOutcomeKind.RejectedBeforePublish,
                PendingOutcomeCode = "test.pending",
            }, terminal.Revision);
        await delivery.ReconcileOnceAsync();

        activator.Count.Should().Be(1, "the rotating one-item budget must reach Immediate work");
    }

    [Fact]
    public async Task Eligibility_is_applied_before_retry_and_claim_quotas()
    {
        var activator = new RecordingNodeActivator();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGateway(static builder => builder.AddCoreFamilies());
        services.AddHpdGatewayManagement(options =>
        {
            options.ManagementAuthorityId = "authority-a";
            options.MaximumTargets = 4;
        });
        services.Replace(ServiceDescriptor.Singleton<IGatewayNodeActivator>(activator));
        await using ServiceProvider provider = services.BuildServiceProvider();
        var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
        var actor = new GatewayManagementActor("actor-a", "test", "manage");
        await commands.ProvisionTargetAsync(new(
            "namespace-a", "node-a", "provision-a", actor, "correlation-a", "epoch-a"));
        var firstCommand = new GatewaySubmitCommand(
            "namespace-a", "node-a", "submit-a", actor, "correlation-b",
            "test", "source-a", "first", ConfigurationBytes(), Activate: true);
        GatewayManagementCommandResult first = await commands.SubmitAsync(firstCommand);
        (await commands.SubmitAsync(firstCommand with
        {
            IdempotencyKey = "submit-b",
            CorrelationId = "correlation-c",
            Description = "second",
            ExpectedDesiredStateToken = first.DesiredStateToken,
        })).State.Should().Be(GatewayManagementCommandState.Accepted);
        BaseSession session = TrustedSession(provider);
        BaseRecord<GatewayDeliveryOutboxItem>[] items = (await session
            .Collection(GatewayDeliveryOutboxItem.Collection).Query()
            .Take(2).ToArrayAsync(2)).RequireValue();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BaseRecord<GatewayDeliveryOutboxItem> future = (await session
            .Collection(GatewayDeliveryOutboxItem.Collection).ReplaceAsync(
                items[0].Id, items[0].Value with
                {
                    State = GatewayDeliveryState.RetryScheduled,
                    NextAttemptAt = now.AddHours(1),
                }, items[0].Revision)).RequireValue();
        BaseRecord<GatewayDeliveryOutboxItem> due = (await session
            .Collection(GatewayDeliveryOutboxItem.Collection).ReplaceAsync(
                items[1].Id, items[1].Value with
                {
                    State = GatewayDeliveryState.RetryScheduled,
                    NextAttemptAt = now.AddMinutes(-1),
                }, items[1].Revision)).RequireValue();
        var delivery = provider.GetRequiredService<IGatewayDeliveryCoordinator>();

        await delivery.ReconcileOnceAsync();
        activator.Count.Should().Be(1, "a future retry must not consume the retry quota before a due retry");

        future = (await session.Collection(GatewayDeliveryOutboxItem.Collection).GetAsync(future.Id)).RequireValue();
        due = (await session.Collection(GatewayDeliveryOutboxItem.Collection).GetAsync(due.Id)).RequireValue();
        await session.Collection(GatewayDeliveryOutboxItem.Collection).ReplaceAsync(
            future.Id, future.Value with
            {
                State = GatewayDeliveryState.Claimed,
                NextAttemptAt = null,
                ClaimId = "live",
                ClaimExpiresAt = now.AddHours(1),
            }, future.Revision);
        await session.Collection(GatewayDeliveryOutboxItem.Collection).ReplaceAsync(
            due.Id, due.Value with
            {
                State = GatewayDeliveryState.Claimed,
                NextAttemptAt = null,
                ClaimId = "expired",
                ClaimExpiresAt = now.AddMinutes(-1),
            }, due.Revision);

        await delivery.ReconcileOnceAsync();
        activator.Count.Should().Be(2, "a live claim must not consume the claim quota before an expired claim");
    }

    [Fact]
    public async Task Purge_rejects_current_and_transitively_referenced_records()
    {
        var activator = new RecordingNodeActivator();
        await WithSqlite(async provider =>
        {
            var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
            var actor = new GatewayManagementActor("actor-a", "test", "manage");
            await commands.ProvisionTargetAsync(new(
                "namespace-a", "node-a", "provision-a", actor, "correlation-a", "epoch-a"));
            ImmutableArray<byte> configuration = ConfigurationBytes();
            var firstCommand = new GatewaySubmitCommand(
                "namespace-a", "node-a", "submit-a", actor, "correlation-b",
                "test", "source-a", "first", configuration, Activate: true);
            GatewayManagementCommandResult first = await commands.SubmitAsync(firstCommand);
            GatewayManagementCommandResult second = await commands.SubmitAsync(firstCommand with
            {
                IdempotencyKey = "submit-b",
                CorrelationId = "correlation-c",
                Description = "second",
                ExpectedDesiredStateToken = first.DesiredStateToken,
                Activate = true,
            });
            second.State.Should().Be(GatewayManagementCommandState.Accepted, second.Code);
            (await provider.GetRequiredService<IGatewayDeliveryCoordinator>().ReconcileOnceAsync())
                .Failed.Should().Be(2);
            BaseSession session = TrustedSession(provider);
            BaseRecord<GatewayAcceptedRevision> firstRevision = (await session
                .Collection(GatewayAcceptedRevision.Collection)
                .GetAsync(RecordId.Create(first.OperationId!))).RequireValue();
            BaseRecord<GatewayAdministrativeAuditRecord> firstAudit = (await session
                .Collection(GatewayAdministrativeAuditRecord.Collection).Query()
                .Where(GatewayAdministrativeAuditRecord.Fields.SubjectId, first.OperationId!)
                .Take(1).ToArrayAsync(1)).RequireValue().Single();
            BaseRecord<GatewayNodeActivationOutcome> outcome = (await session
                .Collection(GatewayNodeActivationOutcome.Collection).Query()
                .Take(1).ToArrayAsync(1)).RequireValue().Single();
            var administration = provider.GetRequiredService<IGatewayManagementAdministration>();

            await AssertProtected(administration, actor, GatewayAuthoritySchema.AcceptedRevisions, first.OperationId!, "revision");
            await AssertProtected(administration, actor, GatewayAuthoritySchema.ValidationRecords, firstRevision.Value.ValidationId, "validation");
            await AssertProtected(administration, actor, GatewayAuthoritySchema.AdministrativeAudit, firstAudit.Id.Value, "audit");
            await AssertProtected(administration, actor, GatewayAuthoritySchema.NodeOutcomes, outcome.Id.Value, "outcome");
            await administration.Invoking(value => value.PurgeAsync(
                    "namespace-a", "receipt-purge", actor, GatewayAuthoritySchema.CommandReceipts,
                    ["gwm.receipt.unavailable"], null).AsTask())
                .Should().ThrowAsync<ArgumentException>()
                .WithMessage("*not Gateway purge-enabled*");
        }, activator);
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

    private static ServiceProvider SqliteProvider(string database, IGatewayNodeActivator? activator = null)
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
        if (activator is not null)
            services.Replace(ServiceDescriptor.Singleton(activator));
        return services.BuildServiceProvider();
    }

    private static ServiceProvider InMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGateway(static builder => builder.AddCoreFamilies());
        services.AddHpdGatewayManagement(options => options.ManagementAuthorityId = "authority-a");
        return services.BuildServiceProvider();
    }

    private static async Task WithSqlite(
        Func<ServiceProvider, Task> action,
        IGatewayNodeActivator? activator = null)
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-gateway-{Guid.NewGuid():N}.db");
        try
        {
            await using ServiceProvider provider = SqliteProvider(database, activator);
            await InitializeSqlite(provider);
            await action(provider);
        }
        finally
        {
            if (File.Exists(database)) File.Delete(database);
            if (File.Exists(database + "-wal")) File.Delete(database + "-wal");
            if (File.Exists(database + "-shm")) File.Delete(database + "-shm");
        }
    }

    [Fact]
    public async Task Application_facade_import_compare_export_and_rollback_preserve_authority_ownership()
    {
        await using ServiceProvider provider = InMemoryProvider();
        var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
        var application = provider.GetRequiredService<IGatewayManagementApplication>();
        var actor = new GatewayManagementActor("actor-a", "scheme-a", "policy-a");
        (await commands.ProvisionLocalTargetAsync(new(
            "namespace-a", "node-a", "provision", actor, "correlation-a"))).IsAccepted.Should().BeTrue();

        GatewayManagementCommandResult imported = await application.ImportAsync(new(
            "namespace-a", "node-a", "import-a", actor, "correlation-a",
            ConfigurationBytes(), "imported", null, true, "import", "artifact-a"));
        imported.IsAccepted.Should().BeTrue(imported.Code);
        imported.DesiredStateToken.Should().NotBeNull();

        GatewayApplicationReadResult<GatewayRevisionExport> exported = await application.ExportAsync(
            "namespace-a", "node-a", imported.OperationId!);
        exported.State.Should().Be(GatewayApplicationReadState.Found);
        exported.Value!.Utf8Configuration.Should().NotBeEmpty();

        GatewayApplicationReadResult<GatewayRevisionComparison> compared = await application.CompareAsync(
            "namespace-a", "node-a", imported.OperationId!, imported.OperationId!);
        compared.Value!.Equivalent.Should().BeTrue();
        compared.Value.Differences.Should().BeEmpty();

        GatewayManagementCommandResult rollback = await application.RollbackAsync(new(
            "namespace-a", "node-a", imported.OperationId!, "rollback-a", actor,
            "correlation-b", "rollback", imported.DesiredStateToken));
        rollback.IsAccepted.Should().BeTrue(rollback.Code);
        GatewayManagedRecord<GatewayAcceptedRevision>? derived = await provider
            .GetRequiredService<IGatewayManagementReader>()
            .GetRevisionAsync("namespace-a", "node-a", rollback.OperationId!);
        derived!.Value.DerivedFromRevisionId.Should().Be(imported.OperationId);
    }

    [Fact]
    public async Task Revisions_and_idempotency_are_strictly_target_scoped()
    {
        await using ServiceProvider provider = InMemoryProvider();
        var commands = provider.GetRequiredService<IGatewayManagementCommandCoordinator>();
        var reader = provider.GetRequiredService<IGatewayManagementReader>();
        var application = provider.GetRequiredService<IGatewayManagementApplication>();
        var actor = new GatewayManagementActor("actor-a", "scheme-a", "policy-a");
        (await commands.ProvisionLocalTargetAsync(new("namespace-a", "node-a", "provision-a", actor, "correlation-a"))).IsAccepted.Should().BeTrue();
        (await commands.ProvisionLocalTargetAsync(new("namespace-a", "node-b", "provision-b", actor, "correlation-b"))).IsAccepted.Should().BeTrue();

        GatewayManagementCommandResult a = await commands.SubmitAsync(new(
            "namespace-a", "node-a", "shared-key", actor, "correlation-a", "code", "source", null,
            ConfigurationBytes(), Activate: false));
        GatewayManagementCommandResult b = await commands.SubmitAsync(new(
            "namespace-a", "node-b", "shared-key", actor, "correlation-b", "code", "source", null,
            ConfigurationBytes(), Activate: false));

        a.IsAccepted.Should().BeTrue(a.Code);
        b.IsAccepted.Should().BeTrue(b.Code);
        b.OperationId.Should().NotBe(a.OperationId);
        (await reader.GetRevisionAsync("namespace-a", "node-b", a.OperationId!)).Should().BeNull();
        (await application.ExportAsync("namespace-a", "node-b", a.OperationId!)).State
            .Should().Be(GatewayApplicationReadState.NotFound);
        (await reader.ListRevisionsAsync("namespace-a", "node-a", 16)).Items
            .Should().ContainSingle(item => item.Id == a.OperationId);
        (await reader.ListRevisionsAsync("namespace-a", "node-b", 16)).Items
            .Should().ContainSingle(item => item.Id == b.OperationId);
    }

    private static BaseSession TrustedSession(ServiceProvider provider) =>
        provider.GetRequiredService<IBaseSessionFactory>().For(TrustedPrincipal(),
            options => options.Mode = OperationMode.System);

    private static PrincipalContext TrustedPrincipal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectId = "gateway-tests",
        AuthSource = GatewayManagementBasePolicy.TrustedSource,
    };

    private static async Task AssertProtected(
        IGatewayManagementAdministration administration,
        GatewayManagementActor actor,
        string collectionId,
        string recordId,
        string key) =>
        await administration.Invoking(value => value.PurgeAsync(
                "namespace-a", "protected-" + key, actor, collectionId, [recordId], null).AsTask())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*reference closure*");

    private static async Task WaitUntil(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
            await Task.Delay(20, timeout.Token);
    }

    private sealed class RecordingNodeActivator : IGatewayNodeActivator
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public ValueTask<GatewayNodeActivationResult> ActivateAsync(
            GatewayNodeActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(new GatewayNodeActivationResult(
                GatewayNodeActivationState.RejectedBeforeMaterialization,
                null,
                null,
                [new GatewayNodeActivationDiagnostic("test.rejected", "$", "Expected test rejection.")]));
        }
    }

    private static ImmutableArray<byte> ConfigurationBytes()
    {
        var valid = GatewayConfigurationTests.CreateValidConfiguration();
        var configuration = valid with
        {
            Routes = [valid.Routes[0] with { Declarations = new HPD.Gateway.Abstractions.RouteDeclarations() }],
        };
        return ImmutableArray.Create(JsonSerializer.SerializeToUtf8Bytes(
            configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration));
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
