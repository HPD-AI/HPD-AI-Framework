using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

internal interface ISqliteSchemaCommandController
{
    ValueTask ExecuteAsync(SqliteConnection connection, string sql, TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed class DefaultSqliteSchemaCommandController : ISqliteSchemaCommandController
{
    public static DefaultSqliteSchemaCommandController Instance { get; } = new();

    private DefaultSqliteSchemaCommandController()
    {
    }

    public async ValueTask ExecuteAsync(SqliteConnection connection, string sql, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
