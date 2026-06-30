using HPD.Base.StoreConformance.Mutations;

namespace HPD.Base.InMemory.Tests.Conformance;

public sealed class InMemoryPatchReplaceConformanceTests : RecordStorePatchReplaceConformanceTests<InMemoryConformanceFixture>
{
}

public sealed class InMemoryRevisionConformanceTests : RecordStoreRevisionConformanceTests<InMemoryConformanceFixture>
{
}

public sealed class InMemoryCopyIsolationConformanceTests : RecordStoreCopyIsolationConformanceTests<InMemoryConformanceFixture>
{
}
