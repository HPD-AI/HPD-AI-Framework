using FluentAssertions;
using HPD.Base.Application.Tests.Generation;
using HPD.Base.Application.Results;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Testing;
using Xunit;

namespace HPD.Base.Application.Tests.Hosting;

public sealed class BaseTestHostTests
{
    [Fact]
    public async Task TestHostOwnsDeterministicTimeAndTypedSessions()
    {
        DateTimeOffset initial = new(2030, 4, 5, 6, 7, 8, TimeSpan.Zero);
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder
                .UseInMemory()
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
                .UseInMemory()
                .AddCollection(GeneratedProject.Collection));
        var records = host
            .Session(BaseTestPrincipal.System("application-test"))
            .Collection(GeneratedProject.Collection);

        host.Faults.FailNextPostCommitObserver();
        BaseResult<Application.Records.BaseRecord<GeneratedProject>> result =
            await records.CreateAsync(
                new RecordId("project_1"),
                new GeneratedProject
                {
                    OrganizationId = "org_1",
                    Name = "captured",
                });

        BaseSuccess<Application.Records.BaseRecord<GeneratedProject>> success =
            result.Should().BeOfType<
                BaseSuccess<Application.Records.BaseRecord<GeneratedProject>>>()
                .Subject;
        success.Warnings.Should().ContainSingle(
            warning => warning.Code == "base.runtime.events.observerFailed");
        host.Probe.Mutations.Should().ContainSingle(
            mutation => mutation.Resource.RecordId == new RecordId("project_1"));
    }

    [Fact]
    public async Task TestHostCanFailExactlyOneAtomicCommit()
    {
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder
                .UseInMemory()
                .AddCollection(GeneratedProject.Collection));
        var session = host.Session(BaseTestPrincipal.System("application-test"));

        host.Faults.FailNextAtomicCommit();
        var failedBatch = session.Atomic();
        failedBatch.Create(
            GeneratedProject.Collection,
            new RecordId("project_1"),
            new GeneratedProject { OrganizationId = "org_1", Name = "failed" });
        Application.Batches.BaseBatchResult failed =
            (await failedBatch.CommitAsync()).RequireValue();

        failed.Outcome.Should().Be(BaseRecordBatchOutcome.RolledBack);
        failed.Error!.Code.Should().Be("base.testing.atomicCommitFailed");
        (await session.Collection(GeneratedProject.Collection)
                .GetAsync(new RecordId("project_1")))
            .Should().BeOfType<
                BaseFailure<Application.Records.BaseRecord<GeneratedProject>>>();
        host.Probe.Mutations.Should().BeEmpty();

        var committedBatch = session.Atomic();
        committedBatch.Create(
            GeneratedProject.Collection,
            new RecordId("project_1"),
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
                    .UseSqlite(options => options.DataSource = database)
                    .AddCollection(GeneratedProject.Collection));
            var session = host.Session(BaseTestPrincipal.System("application-test"));

            host.Faults.FailNextAtomicCommit();
            var batch = session.Atomic();
            batch.Create(
                GeneratedProject.Collection,
                new RecordId("project_rollback"),
                new GeneratedProject { OrganizationId = "org_1", Name = "rollback" });

            Application.Batches.BaseBatchResult failure =
                (await batch.CommitAsync()).RequireValue();
            failure.Outcome.Should().Be(BaseRecordBatchOutcome.RolledBack);
            failure.Error!.Code.Should().Be("base.testing.atomicCommitFailed");
            (await session.Collection(GeneratedProject.Collection)
                    .GetAsync(new RecordId("project_rollback")))
                .Should().BeOfType<
                    BaseFailure<Application.Records.BaseRecord<GeneratedProject>>>();
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
    public async Task IndeterminateCommitExposesNoBatchItemsButMayHavePersisted()
    {
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder
                .UseInMemory()
                .AddCollection(GeneratedProject.Collection));
        var session = host.Session(BaseTestPrincipal.System("application-test"));

        host.Faults.MakeNextAtomicCommitIndeterminate();
        var batch = session.Atomic();
        batch.Create(
            GeneratedProject.Collection,
            new RecordId("project_indeterminate"),
            new GeneratedProject { OrganizationId = "org_1", Name = "indeterminate" });

        BaseFailure<Application.Batches.BaseBatchResult> failure =
            (await batch.CommitAsync()).Should()
                .BeOfType<BaseFailure<Application.Batches.BaseBatchResult>>()
                .Subject;
        failure.Status.Should().Be(OperationStatus.StoreError);
        failure.Error.Code.Should().Be("base.runtime.batch.indeterminate");
        (await session.Collection(GeneratedProject.Collection)
                .GetAsync(new RecordId("project_indeterminate")))
            .RequireValue()
            .Value.Name.Should().Be("indeterminate");
        host.Probe.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task TestPolicyChangesAreEvaluatedForEveryOperation()
    {
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder
                .UseInMemory()
                .AddCollection(GeneratedProject.Collection));
        var records = host
            .Session(BaseTestPrincipal.System("application-test"))
            .Collection(GeneratedProject.Collection);
        await records.CreateAsync(
            new RecordId("project_1"),
            new GeneratedProject { OrganizationId = "org_1", Name = "visible" });

        host.Policy.DenyAll();
        BaseResult<Application.Records.BaseRecord<GeneratedProject>> denied =
            await records.GetAsync(new RecordId("project_1"));
        denied.Should().BeOfType<
            BaseFailure<Application.Records.BaseRecord<GeneratedProject>>>();

        host.Policy.AllowAll();
        (await records.GetAsync(new RecordId("project_1")))
            .RequireValue()
            .Should().NotBeNull();
    }
}
