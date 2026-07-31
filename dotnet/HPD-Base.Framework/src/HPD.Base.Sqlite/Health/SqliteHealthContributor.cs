using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite;

internal sealed class SqliteHealthContributor : IBaseHealthContributor
{
    private readonly HPDBaseSqliteOptions _options;
    private readonly SqliteRecordStore _store;

    public SqliteHealthContributor(
        IOptions<HPDBaseSqliteOptions> options,
        SqliteRecordStore store)
    {
        _options = options.Value;
        _store = store;
    }

    public string Id => _options.HealthRefId;

    public async ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var status = HealthStatus.Healthy;
        var summary = "SQLite store is reachable.";
        string? journalMode = null;
        string[] missing = [];
        var quarantinedMutations = _store.QuarantinedMutationCount;
        try
        {
            var factory = new SqliteConnectionFactory(_options);
            SqliteConnectionFactory.InitializeBatteries(_options);
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                factory.BuildConnectionString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            journalMode = await factory.GetJournalModeAsync(connection, cancellationToken).ConfigureAwait(false);
            var schema = new SqliteSchemaInitializer(_options);
            if (_options.AutoInitialize && quarantinedMutations == 0)
            {
                await schema.InitializeAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            missing = await schema.GetMissingSchemaPartsAsync(connection, cancellationToken).ConfigureAwait(false);
            if (missing.Length != 0)
            {
                status = _options.FailIfSchemaMissing
                    ? HealthStatus.Unhealthy
                    : HealthStatus.Degraded;
                summary = "SQLite provider-owned schema is missing required parts.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            status = HealthStatus.Unhealthy;
            summary = "SQLite store is not reachable.";
        }

        if (quarantinedMutations != 0 && status == HealthStatus.Healthy)
        {
            status = HealthStatus.Degraded;
            summary = "SQLite has indeterminate mutation work in quarantine.";
        }

        return
        [
            new HealthDescriptor
            {
                Id = _options.HealthRefId,
                Scope = HealthScope.Store,
                TargetRef = _options.StoreId,
                Status = status,
                CheckedAt = DateTimeOffset.UtcNow,
                Summary = summary,
                PublicSafe = false,
                Visibility = VisibilityLevel.Admin,
                Metrics =
                [
                    new HealthMetric { Name = "schemaPrefix", Kind = HealthMetricValueKind.Text, TextValue = _options.SchemaPrefix },
                    new HealthMetric { Name = "journalMode", Kind = HealthMetricValueKind.Text, TextValue = journalMode },
                    new HealthMetric { Name = "missingSchemaParts", Kind = HealthMetricValueKind.Number, NumberValue = missing.Length },
                    new HealthMetric { Name = "quarantinedMutations", Kind = HealthMetricValueKind.Number, NumberValue = quarantinedMutations }
                ],
                Dependencies = missing.Length == 0
                    ? null
                    : missing.Select(part => new HealthDependency { Id = part, Kind = "sqlite.schema", Status = HealthStatus.Unhealthy }).ToArray()
            }
        ];
    }
}
