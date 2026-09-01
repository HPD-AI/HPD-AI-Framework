using System.Collections.Immutable;
using HPD.Base.Sqlite;
using HPD.Base.Testing;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Tests.Schema;

public sealed class BaseLogicalIndexProviderCertificationTests
{
    [Fact]
    public async Task InMemory_executes_the_complete_18_case_report()
    {
        BaseLogicalIndexCertificationReport report = await
            BaseLogicalIndexProviderCertification.RunAsync(new InMemoryFixture());

        Assert.True(BaseLogicalIndexProviderContract.ValidateReport(report));
        Assert.True(BaseLogicalIndexFrozenReportCodec.Encode(
                BaseLogicalIndexBuiltInCertification.LoadFrozenExecutedReport("inmemory"))
            .SequenceEqual(BaseLogicalIndexFrozenReportCodec.Encode(report)));
        Assert.Equal(BaseLogicalIndexProviderContract.CaseIds, report.Cases.Select(
            static item => item.Id));
        Assert.All(report.Cases, item =>
        {
            (OperationStatus status, string? error) =
                BaseLogicalIndexProviderContract.ExpectedOutcome(item.Id);
            Assert.Equal(status, item.ObservedStatus);
            Assert.Equal(error, item.ObservedErrorCode);
        });
    }

    [Fact]
    public async Task Sqlite_executes_the_complete_18_case_report()
    {
        BaseLogicalIndexCertificationReport report = await
            BaseLogicalIndexProviderCertification.RunAsync(new SqliteFixture());

        Assert.True(BaseLogicalIndexProviderContract.ValidateReport(report));
        Assert.True(BaseLogicalIndexFrozenReportCodec.Encode(
                BaseLogicalIndexBuiltInCertification.LoadFrozenExecutedReport("sqlite"))
            .SequenceEqual(BaseLogicalIndexFrozenReportCodec.Encode(report)));
        Assert.Equal(BaseLogicalIndexProviderContract.CaseIds, report.Cases.Select(
            static item => item.Id));
        Assert.All(report.Cases, item =>
        {
            (OperationStatus status, string? error) =
                BaseLogicalIndexProviderContract.ExpectedOutcome(item.Id);
            Assert.Equal(status, item.ObservedStatus);
            Assert.Equal(error, item.ObservedErrorCode);
        });
    }

    private sealed class InMemoryFixture : IBaseLogicalIndexCertificationFixture
    {
        public BaseLogicalIndexCertificationFixtureIdentity Identity { get; } = new()
        {
            ProviderId = "hpd.base.inMemory.logicalIndexes",
            ProviderVersion = 1,
            StoreProviderKind = "inmemory",
            GenerationConflictStrategy =
                BaseLogicalIndexGenerationConflictStrategy.OptimisticCapture,
            NativeDependencyReceipts = [],
        };

        public ValueTask<BaseLogicalIndexCertificationRoot> CreateRootAsync(
            BaseLogicalIndexCertificationRootRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new
                BaseLogicalIndexCertificationRoot(InMemoryProviderInstaller.Create(options =>
                {
                    if (request.CertificationCapability is not null)
                        options.LogicalIndexCertificationCapability =
                            request.CertificationCapability;
                }), null));
    }

    private sealed class SqliteFixture : IBaseLogicalIndexCertificationFixture
    {
        public BaseLogicalIndexCertificationFixtureIdentity Identity { get; } = new()
        {
            ProviderId = "hpd.base.sqlite.logicalIndexes",
            ProviderVersion = 1,
            StoreProviderKind = "sqlite",
            GenerationConflictStrategy =
                BaseLogicalIndexGenerationConflictStrategy.WriteOwnership,
            NativeDependencyReceipts =
            [
                $"microsoft.data.sqlite:{typeof(SqliteConnection).Assembly.GetName().Version}",
                $"runtime:{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
            ],
        };

        public ValueTask<BaseLogicalIndexCertificationRoot> CreateRootAsync(
            BaseLogicalIndexCertificationRootRequest request,
            CancellationToken cancellationToken = default)
        {
            string database = Path.Combine(Path.GetTempPath(),
                $"hpd-base-l80-report-{Guid.NewGuid():N}.db");
            HPDBaseStoreProvider provider = SqliteStore.Configure(options =>
            {
                options.DataSource = database;
                options.StoreId = "base-cert-sqlite";
                options.BusyTimeout = TimeSpan.FromMilliseconds(1);
                if (request.CertificationCapability is not null)
                    options.LogicalIndexCertificationCapability =
                        request.CertificationCapability;
            });
            return ValueTask.FromResult(new BaseLogicalIndexCertificationRoot(
                provider,
                "base-cert-sqlite",
                token => AcquireWriteOwnerAsync(database, token),
                () =>
                {
                    SqliteConnection.ClearAllPools();
                    if (File.Exists(database)) File.Delete(database);
                    return ValueTask.CompletedTask;
                }));
        }

        private static async ValueTask<IAsyncDisposable> AcquireWriteOwnerAsync(
            string database,
            CancellationToken cancellationToken)
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = database }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "BEGIN IMMEDIATE;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new WriteOwner(connection);
        }

        private sealed class WriteOwner(SqliteConnection connection) : IAsyncDisposable
        {
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await using SqliteCommand command = connection.CreateCommand();
                    command.CommandText = "ROLLBACK;";
                    await command.ExecuteNonQueryAsync();
                }
                finally
                {
                    await connection.DisposeAsync();
                }
            }
        }
    }
}
