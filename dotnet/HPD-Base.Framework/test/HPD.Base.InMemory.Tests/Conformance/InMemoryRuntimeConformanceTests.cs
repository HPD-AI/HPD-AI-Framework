using HPD.Base.StoreConformance.Runtime;

namespace HPD.Base.InMemory.Tests.Conformance;

public sealed class InMemoryRuntimeRegistrationConformanceTests : RuntimeStoreRegistrationConformanceTests<InMemoryConformanceFixture>
{
}

public sealed class InMemoryRuntimeCapabilityGateConformanceTests : RuntimeStoreCapabilityGateConformanceTests<InMemoryConformanceFixture>
{
}

public sealed class InMemoryRuntimeQueryConformanceTests : RuntimeStoreQueryConformanceTests<InMemoryConformanceFixture>
{
}

public sealed class InMemoryRuntimeDescriptorHonestyConformanceTests : RuntimeStoreDescriptorHonestyConformanceTests<InMemoryConformanceFixture>
{
}

public sealed class InMemoryRuntimePolicyConformanceTests : RuntimeStorePolicyConformanceTests<InMemoryConformanceFixture>
{
}

public sealed class InMemoryRuntimeResultNormalizationConformanceTests : RuntimeStoreResultNormalizationConformanceTests<InMemoryConformanceFixture>
{
}

public sealed class InMemoryRuntimeEventConformanceTests : RuntimeStoreEventConformanceTests<InMemoryConformanceFixture>
{
}
