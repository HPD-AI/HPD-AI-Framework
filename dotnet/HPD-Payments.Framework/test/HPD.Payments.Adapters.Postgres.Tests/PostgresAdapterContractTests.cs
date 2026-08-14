using HPD.Payments.Adapters.Postgres;

namespace HPD.Payments.Adapters.Postgres.Tests;

/// <summary>Executes provider-independent PostgreSQL contract assertions without simulating a database.</summary>
public static class PostgresAdapterContractTests
{
    /// <summary>Runs the bounded static contract suite.</summary>
    /// <returns>Zero when every assertion passes.</returns>
    public static int Main()
    {
        var ddl = PostgresSql.CreateSchema("hpd_payments");
        Assert(ddl.Contains("CHECK (generation > 0)", StringComparison.Ordinal), "generation guard missing");
        Assert(ddl.Contains("UNIQUE (scope, authority, subject, semantic_digest)", StringComparison.Ordinal), "replay key missing");
        Assert(PostgresSql.LockOwner.EndsWith("FOR UPDATE", StringComparison.Ordinal), "owner conflict lock missing");
        Assert(PostgresSql.LockRelationEndpoints.Contains("ORDER BY authority, subject FOR UPDATE", StringComparison.Ordinal), "deterministic endpoint locks missing");
        Assert(PostgresSql.ClaimContinuations.Contains("FOR UPDATE SKIP LOCKED", StringComparison.Ordinal), "worker contention clause missing");
        Assert(!ddl.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase), "schema must remain append-only");
        Assert(!ddl.Contains("broker", StringComparison.OrdinalIgnoreCase) && !ddl.Contains("provider_call", StringComparison.OrdinalIgnoreCase), "external effects entered atomic schema");
        return 0;
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
