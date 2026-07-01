using HPD.Base.StoreConformance.Runtime;

namespace HPD.Base.Sqlite.Tests.Conformance;

public sealed class SqliteRuntimeRegistrationConformanceTests : RuntimeStoreRegistrationConformanceTests<SqliteConformanceFixture> { }

public sealed class SqliteRuntimeCapabilityGateConformanceTests : RuntimeStoreCapabilityGateConformanceTests<SqliteConformanceFixture> { }

public sealed class SqliteRuntimeQueryConformanceTests : RuntimeStoreQueryConformanceTests<SqliteConformanceFixture> { }

public sealed class SqliteRuntimeDescriptorHonestyConformanceTests : RuntimeStoreDescriptorHonestyConformanceTests<SqliteConformanceFixture> { }

public sealed class SqliteRuntimePolicyConformanceTests : RuntimeStorePolicyConformanceTests<SqliteConformanceFixture> { }

public sealed class SqliteRuntimeResultNormalizationConformanceTests : RuntimeStoreResultNormalizationConformanceTests<SqliteConformanceFixture> { }

public sealed class SqliteRuntimeEventConformanceTests : RuntimeStoreEventConformanceTests<SqliteConformanceFixture> { }
