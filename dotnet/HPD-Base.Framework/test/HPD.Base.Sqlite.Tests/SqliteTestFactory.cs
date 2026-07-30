using HPD.Base.Sqlite.Configuration;
using HPD.Base.Sqlite.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Base.Sqlite.Tests;

internal static class SqliteTestFactory
{
    public static SqliteRecordStore Create(
        HPDBaseSqliteOptions? options = null,
        TimeProvider? timeProvider = null,
        ISqliteTransactionController? transactions = null,
        ISqliteSessionOperationController? sessionOperations = null,
        ISqliteTransactionResourceDisposer? transactionResourceDisposer = null)
    {
        options ??= new HPDBaseSqliteOptions();
        return timeProvider is null
            && transactions is null
            && sessionOperations is null
            && transactionResourceDisposer is null
            ? new SqliteRecordStore(options, NullLoggerFactory.Instance)
            : new SqliteRecordStore(
                options,
                NullLoggerFactory.Instance,
                timeProvider ?? TimeProvider.System,
                transactions,
                sessionOperations,
                transactionResourceDisposer);
    }
}
