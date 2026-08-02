using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

internal interface ISqliteTransactionResourceDisposer
{
    /// <summary>Executes the dispose async operation.</summary>
    ValueTask DisposeAsync(
        SqliteTransaction transaction,
        SqliteConnection connection);
}

internal sealed class DefaultSqliteTransactionResourceDisposer
    : ISqliteTransactionResourceDisposer
{
    /// <summary>Gets the instance.</summary>
    public static DefaultSqliteTransactionResourceDisposer Instance { get; } = new();

    private DefaultSqliteTransactionResourceDisposer()
    {
    }

    /// <summary>Executes the dispose async operation.</summary>
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
