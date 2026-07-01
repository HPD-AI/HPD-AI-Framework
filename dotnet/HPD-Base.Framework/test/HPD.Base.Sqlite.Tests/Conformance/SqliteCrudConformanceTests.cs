using HPD.Base.StoreConformance.Crud;

namespace HPD.Base.Sqlite.Tests.Conformance;

public sealed class SqliteCrudConformanceTests : RecordStoreCrudConformanceTests<SqliteConformanceFixture> { }

public sealed class SqliteCrudUnsupportedConformanceTests : RecordStoreCrudUnsupportedConformanceTests<SqliteConformanceFixture> { }
