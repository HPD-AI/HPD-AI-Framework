using HPD.Base.StoreConformance.Crud;

namespace HPD.Base.InMemory.Tests.Conformance;

public sealed class InMemoryCrudConformanceTests : RecordStoreCrudConformanceTests<InMemoryConformanceFixture>
{
}

public sealed class InMemoryCrudUnsupportedConformanceTests : RecordStoreCrudUnsupportedConformanceTests<InMemoryConformanceFixture>
{
}
