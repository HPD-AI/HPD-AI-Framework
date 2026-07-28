using HPD.Base.Sqlite.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Base.Sqlite.Tests;

internal static class SqliteTestFactory
{
    public static SqliteRecordStore Create(
        HPDBaseSqliteOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        options ??= new HPDBaseSqliteOptions();
        return timeProvider is null
            ? new SqliteRecordStore(options, NullLoggerFactory.Instance)
            : new SqliteRecordStore(options, NullLoggerFactory.Instance, timeProvider);
    }
}
