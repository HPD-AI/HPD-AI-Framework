using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite.Internal;

internal interface ISqliteTransactionController
{
    SqliteTransaction BeginImmediate(SqliteConnection connection);

    ValueTask CommitAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken);

    ValueTask RollbackAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken);
}

internal sealed class DefaultSqliteTransactionController : ISqliteTransactionController
{
    public static DefaultSqliteTransactionController Instance { get; } = new();

    private DefaultSqliteTransactionController()
    {
    }

    public SqliteTransaction BeginImmediate(SqliteConnection connection) =>
        connection.BeginTransaction(deferred: false);

    public ValueTask CommitAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken) =>
        new(transaction.CommitAsync(cancellationToken));

    public ValueTask RollbackAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken) =>
        new(transaction.RollbackAsync(cancellationToken));
}
