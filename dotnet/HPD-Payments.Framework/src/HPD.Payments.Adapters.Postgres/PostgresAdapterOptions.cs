using System.Data.Common;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Adapters.Postgres;

/// <summary>Provides the bounded PostgreSQL connection and topology inputs used by one adapter instance.</summary>
/// <remarks>The supplied data source must be a real PostgreSQL provider. The adapter never infers certification from its presence.</remarks>
public sealed record PostgresAdapterOptions
{
    /// <summary>Gets the provider-owned data source used to open physical PostgreSQL connections.</summary>
    public DbDataSource DataSource { get; }

    /// <summary>Gets the exact topology/configuration revision bound into every operation.</summary>
    public Revision TopologyRevision { get; }

    /// <summary>Gets the bounded SQL schema containing adapter-owned tables.</summary>
    public string Schema { get; }

    /// <summary>Gets the maximum duration of one database command.</summary>
    public TimeSpan CommandTimeout { get; }

    /// <summary>Creates immutable PostgreSQL adapter options.</summary>
    /// <param name="dataSource">A PostgreSQL <see cref="DbDataSource"/>.</param>
    /// <param name="topologyRevision">Exact topology revision.</param>
    /// <param name="schema">Lower-case PostgreSQL identifier.</param>
    /// <param name="commandTimeout">Positive timeout no greater than five minutes.</param>
    /// <exception cref="ArgumentException">A revision, schema, provider, or timeout is invalid.</exception>
    public PostgresAdapterOptions(DbDataSource dataSource, Revision topologyRevision, string schema = "hpd_payments", TimeSpan? commandTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(schema);
        var timeout = commandTimeout ?? TimeSpan.FromSeconds(30);
        if (!topologyRevision.IsValid || schema.Length is < 1 or > 63 || schema.Any(static c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) ||
            timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentException("PostgreSQL options require a valid revision, identifier, provider, and bounded timeout.");
        DataSource = dataSource;
        TopologyRevision = topologyRevision;
        Schema = schema;
        CommandTimeout = timeout;
    }
}
