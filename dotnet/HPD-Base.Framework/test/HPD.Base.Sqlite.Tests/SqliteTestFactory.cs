using HPD.Base.Sqlite.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Base.Sqlite.Tests;

internal static class SqliteTestFactory
{
    public static SqliteRecordStore Create(HPDBaseSqliteOptions? options = null) =>
        new(options ?? new HPDBaseSqliteOptions(), NullLoggerFactory.Instance);
}
