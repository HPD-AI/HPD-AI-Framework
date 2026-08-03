using HPD.Base.StoreConformance.Crud;
using HPD.Base.StoreConformance.Mutations;

namespace HPD.Base.Sqlite.Tests.Conformance;

public sealed class SqliteCrudConformanceTests : RecordStoreCrudConformanceTests<SqliteConformanceFixture> { }

public sealed class SqliteCrudUnsupportedConformanceTests : RecordStoreCrudUnsupportedConformanceTests<SqliteConformanceFixture> { }

public sealed class SqliteMutationModeConformanceTests : RecordStoreMutationModeConformanceTests<SqliteConformanceFixture> { }
