using FluentAssertions;
using HPD.Base.Tests.Application.Generation;
using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Base.Testing;
using Xunit;

namespace HPD.Base.Tests.Application.Hosting;

public sealed class BaseTestHostTests
{
    [Fact]
    public async Task TestHostOwnsDeterministicTimeAndTypedSessions()
    {
        DateTimeOffset initial = new(2030, 4, 5, 6, 7, 8, TimeSpan.Zero);
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder

                .AddCollection(GeneratedProject.Collection),
            initial);

        host.Time.GetUtcNow().Should().Be(initial);
        host.Time.Advance(TimeSpan.FromMinutes(2));
        host.Time.GetUtcNow().Should().Be(initial.AddMinutes(2));

        host.Session(BaseTestPrincipal.System("application-test"))
            .Collection(GeneratedProject.Collection)
            .Should().NotBeNull();
    }

    [Fact]
    public async Task TestHostCapturesMutationsAndInjectsObserverFailures()
    {
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder

                .AddCollection(GeneratedProject.Collection));
        var records = host
            .Session(BaseTestPrincipal.System("application-test"))
            .Collection(GeneratedProject.Collection);

        host.Faults.FailNextPostCommitObserver();
        BaseResult<BaseRecord<GeneratedProject>> result =
            await records.CreateAsync(
                RecordId.Create("project_1"),
                new GeneratedProject
                {
                    OrganizationId = "org_1",
                    Name = "captured",
                });

        BaseSuccess<BaseRecord<GeneratedProject>> success =
            result.Should().BeOfType<
                BaseSuccess<BaseRecord<GeneratedProject>>>()
                .Subject;
        success.Warnings.Should().ContainSingle(
            warning => warning.Code == "base.runtime.events.observerFailed");
        host.Probe.Mutations.Should().ContainSingle(
            mutation => mutation.Resource.RecordId == RecordId.Create("project_1"));
    }

    [Fact]
    public async Task TestHostCanFailExactlyOneAtomicCommit()
    {
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder

                .AddCollection(GeneratedProject.Collection));
        var session = host.Session(BaseTestPrincipal.System("application-test"));

        host.Faults.FailNextAtomicCommit();
        var failedBatch = session.Atomic();
        failedBatch.Create(
            GeneratedProject.Collection,
            RecordId.Create("project_1"),
            new GeneratedProject { OrganizationId = "org_1", Name = "failed" });
        BaseBatchResult failed =
            (await failedBatch.CommitAsync()).RequireValue();

        failed.Outcome.Should().Be(BaseRecordBatchOutcome.RolledBack);
        failed.Error!.Code.Should().Be("base.testing.atomicCommitFailed");
        (await session.Collection(GeneratedProject.Collection)
                .GetAsync(RecordId.Create("project_1")))
            .Should().BeOfType<
                BaseFailure<BaseRecord<GeneratedProject>>>();
        host.Probe.Mutations.Should().BeEmpty();

        var committedBatch = session.Atomic();
        committedBatch.Create(
            GeneratedProject.Collection,
            RecordId.Create("project_1"),
            new GeneratedProject { OrganizationId = "org_1", Name = "committed" });
        (await committedBatch.CommitAsync())
            .RequireValue()
            .RequireCommitted();
    }

    [Fact]
    public async Task AtomicCommitFailureRollsBackSqliteRecordsAndJournal()
    {
        string database = Path.Combine(
            Path.GetTempPath(),
            $"hpd-base-testing-{Guid.NewGuid():N}.db");
        try
        {
            await using BaseTestHost host = await BaseTestHost.CreateAsync(
                builder => builder
                    .UseStore(SqliteStore.Configure(options => options.DataSource = database))
                    .AddCollection(GeneratedProject.Collection));
            var session = host.Session(BaseTestPrincipal.System("application-test"));

            host.Faults.FailNextAtomicCommit();
            var batch = session.Atomic();
            batch.Create(
                GeneratedProject.Collection,
                RecordId.Create("project_rollback"),
                new GeneratedProject { OrganizationId = "org_1", Name = "rollback" });

            BaseBatchResult failure =
                (await batch.CommitAsync()).RequireValue();
            failure.Outcome.Should().Be(BaseRecordBatchOutcome.RolledBack);
            failure.Error!.Code.Should().Be("base.testing.atomicCommitFailed");
            (await session.Collection(GeneratedProject.Collection)
                    .GetAsync(RecordId.Create("project_rollback")))
                .Should().BeOfType<
                    BaseFailure<BaseRecord<GeneratedProject>>>();
            (await host.JournalAsync()).Should().BeEmpty();
            host.Probe.Mutations.Should().BeEmpty();
        }
        finally
        {
            File.Delete(database);
            File.Delete(database + "-shm");
            File.Delete(database + "-wal");
        }
    }

    [Fact]
    public async Task SqliteTestFaultsPreserveTheProductionProviderCapabilitySurface()
    {
        string database = Path.Combine(
            Path.GetTempPath(),
            $"hpd-base-testing-capabilities-{Guid.NewGuid():N}.db");
        try
        {
            await using BaseTestHost host = await BaseTestHost.CreateAsync(
                builder => builder
                    .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
                    {
                        Id = 17,
                        Key = Enumerable.Repeat((byte)0x6D, 32).ToArray(),
                        IssueNotBefore = DateTimeOffset.UnixEpoch,
                    })
                    .UseStore(SqliteStore.Configure(options =>
                    {
                        options.DataSource = database;
                        options.AdministrationEnabled = true;
                    }))
                    .AddCollection(GeneratedProject.Collection));

            IRecordStoreRegistry registry = host.GetRequiredService<IRecordStoreRegistry>();
            RecordStoreRegistration registration = registry.GetRegistration("sqlite")!;

            registry.GetRegistrations().Should().ContainSingle();
            registration.Store.Should().BeAssignableTo<IBaseSchemaStore>();
            registration.Store.Should().BeAssignableTo<IRelationalReadStore>();
            registration.Store.Should().BeAssignableTo<IConsistentRecordIncludeStore>();
            registration.Store.Should().BeAssignableTo<IRecordStoreAdministration>();
            registration.Store.Should().BeAssignableTo<IBaseSubjectAdministration>();
            registration.Store.Should().BeAssignableTo<IBaseSubjectPublicationStore>();
            registration.Store.Should().BeAssignableTo<IBaseSubjectValidationPlanReceiptStore>();
            registration.Store.Should().BeAssignableTo<IBaseActivationProvider>();
            registration.AtomicExecutionStore.Should().NotBeNull();
            registration.AtomicExecutionStore.Should().NotBeSameAs(registration.Store);

            OperationResult<BaseSchemaPlan> plan = await host
                .GetRequiredService<IBaseSchemaManager>()
                .PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" });

            plan.IsSuccess().Should().BeTrue(plan.Error?.Code);

            IHPDBaseAdministration administration = host.GetRequiredService<IHPDBaseAdministration>();
            administration.Capability.Should().Match<BaseAdministrationCapability>(capability =>
                capability.Durable && capability.Backup && capability.Validate && capability.Restore);
            PrincipalContext principal = BaseTestPrincipal.System("administration-test");
            BaseCollectionSession<GeneratedProject> collection = host.Session(principal)
                .Collection(GeneratedProject.Collection);
            (await collection.CreateAsync(RecordId.Create("before-backup"), new GeneratedProject
            {
                OrganizationId = "org_1",
                Name = "retained",
            })).RequireValue();

            using var artifact = new MemoryStream();
            BaseBackupManifest manifest = (await administration.CreateBackupAsync(
                artifact,
                new BaseBackupRequest { StoreId = "sqlite", Principal = principal })).RequireValue();
            artifact.Position = 0;
            (await administration.ValidateBackupAsync(
                artifact,
                new BaseBackupValidationRequest
                {
                    StoreId = "sqlite",
                    Principal = principal,
                    ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                })).RequireValue();

            host.Faults.FailNextAtomicCommit();
            BaseBatchBuilder failed = host.Session(principal).Atomic();
            failed.Create(
                GeneratedProject.Collection,
                RecordId.Create("failed-after-backup"),
                new GeneratedProject { OrganizationId = "org_1", Name = "rolled-back" });
            (await failed.CommitAsync()).RequireValue().Outcome.Should().Be(BaseRecordBatchOutcome.RolledBack);

            (await collection.CreateAsync(RecordId.Create("after-backup"), new GeneratedProject
            {
                OrganizationId = "org_1",
                Name = "removed-by-restore",
            })).RequireValue();
            artifact.Position = 0;
            BaseRestoreResult restored = (await administration.RestoreAsync(
                artifact,
                new BaseRestoreRequest
                {
                    StoreId = "sqlite",
                    Principal = principal,
                    ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                    ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                    IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                    RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                    ConfirmDestructiveReplacement = true,
                    ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
                })).RequireValue();

            restored.RestoreEpoch.Should().Be(manifest.RestoreEpoch + 1);
            (await collection.GetAsync(RecordId.Create("before-backup"))).Should()
                .BeOfType<BaseSuccess<BaseRecord<GeneratedProject>>>();
            (await collection.GetAsync(RecordId.Create("failed-after-backup"))).Should()
                .BeOfType<BaseFailure<BaseRecord<GeneratedProject>>>();
            (await collection.GetAsync(RecordId.Create("after-backup"))).Should()
                .BeOfType<BaseFailure<BaseRecord<GeneratedProject>>>();
        }
        finally
        {
            File.Delete(database);
            File.Delete(database + "-shm");
            File.Delete(database + "-wal");
        }
    }

    [Fact]
    public async Task IndeterminateCommitExposesNoBatchItemsButMayHavePersisted()
    {
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder

                .AddCollection(GeneratedProject.Collection));
        var session = host.Session(BaseTestPrincipal.System("application-test"));

        host.Faults.MakeNextAtomicCommitIndeterminate();
        var batch = session.Atomic();
        batch.Create(
            GeneratedProject.Collection,
            RecordId.Create("project_indeterminate"),
            new GeneratedProject { OrganizationId = "org_1", Name = "indeterminate" });

        BaseFailure<BaseBatchResult> failure =
            (await batch.CommitAsync()).Should()
                .BeOfType<BaseFailure<BaseBatchResult>>()
                .Subject;
        failure.Status.Should().Be(OperationStatus.StoreError);
        failure.Error.Code.Should().Be("base.runtime.batch.indeterminate");
        (await session.Collection(GeneratedProject.Collection)
                .GetAsync(RecordId.Create("project_indeterminate")))
            .RequireValue()
            .Value.Name.Should().Be("indeterminate");
        host.Probe.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task TestPolicyChangesAreEvaluatedForEveryOperation()
    {
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder

                .AddCollection(GeneratedProject.Collection));
        var records = host
            .Session(BaseTestPrincipal.System("application-test"))
            .Collection(GeneratedProject.Collection);
        await records.CreateAsync(
            RecordId.Create("project_1"),
            new GeneratedProject { OrganizationId = "org_1", Name = "visible" });

        host.Policy.DenyAll();
        BaseResult<BaseRecord<GeneratedProject>> denied =
            await records.GetAsync(RecordId.Create("project_1"));
        denied.Should().BeOfType<
            BaseFailure<BaseRecord<GeneratedProject>>>();

        host.Policy.AllowAll();
        (await records.GetAsync(RecordId.Create("project_1")))
            .RequireValue()
            .Should().NotBeNull();
    }
}
