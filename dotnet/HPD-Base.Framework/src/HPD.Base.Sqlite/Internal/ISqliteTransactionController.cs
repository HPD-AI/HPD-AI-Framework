using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

internal interface ISqliteTransactionController
{
    /// <summary>Executes the begin immediate operation.</summary>
    SqliteTransaction BeginImmediate(SqliteConnection connection);

    /// <summary>Executes the commit async operation.</summary>
    ValueTask CommitAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken);

    /// <summary>Executes the rollback async operation.</summary>
    ValueTask RollbackAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken);
}

internal sealed class DefaultSqliteTransactionController : ISqliteTransactionController
{
    /// <summary>Gets the instance.</summary>
    public static DefaultSqliteTransactionController Instance { get; } = new();

    private DefaultSqliteTransactionController()
    {
    }

    /// <summary>Executes the begin immediate operation.</summary>
    public SqliteTransaction BeginImmediate(SqliteConnection connection) =>
        connection.BeginTransaction(deferred: false);

    /// <summary>Executes the commit async operation.</summary>
    public ValueTask CommitAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken) =>
        new(transaction.CommitAsync(cancellationToken));

    /// <summary>Executes the rollback async operation.</summary>
    public ValueTask RollbackAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken) =>
        new(transaction.RollbackAsync(cancellationToken));
}
