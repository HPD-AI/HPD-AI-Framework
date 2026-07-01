using HPD.Base.StoreConformance.Mutations;

namespace HPD.Base.Sqlite.Tests.Conformance;

public sealed class SqlitePatchReplaceConformanceTests : RecordStorePatchReplaceConformanceTests<SqliteConformanceFixture> { }

public sealed class SqliteRevisionConformanceTests : RecordStoreRevisionConformanceTests<SqliteConformanceFixture> { }

public sealed class SqliteCopyIsolationConformanceTests : RecordStoreCopyIsolationConformanceTests<SqliteConformanceFixture> { }
