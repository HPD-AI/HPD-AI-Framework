using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

internal interface ISqliteTransactionResourceDisposer
{
    ValueTask DisposeAsync(
        SqliteTransaction transaction,
        SqliteConnection connection);
}

internal sealed class DefaultSqliteTransactionResourceDisposer
    : ISqliteTransactionResourceDisposer
{
    public static DefaultSqliteTransactionResourceDisposer Instance { get; } = new();

    private DefaultSqliteTransactionResourceDisposer()
    {
    }

    public async ValueTask DisposeAsync(
        SqliteTransaction transaction,
        SqliteConnection connection)
    {
        try
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
