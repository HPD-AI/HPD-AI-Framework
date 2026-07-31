using HPD.Base.StoreConformance.Runtime;

namespace HPD.Base.Tests.Volatile.Conformance;

public sealed class VolatileRuntimeRegistrationConformanceTests : RuntimeStoreRegistrationConformanceTests<VolatileConformanceFixture>
{
}

public sealed class VolatileRuntimeCapabilityGateConformanceTests : RuntimeStoreCapabilityGateConformanceTests<VolatileConformanceFixture>
{
}

public sealed class VolatileRuntimeQueryConformanceTests : RuntimeStoreQueryConformanceTests<VolatileConformanceFixture>
{
}

public sealed class VolatileRuntimeDescriptorHonestyConformanceTests : RuntimeStoreDescriptorHonestyConformanceTests<VolatileConformanceFixture>
{
}

public sealed class VolatileRuntimePolicyConformanceTests : RuntimeStorePolicyConformanceTests<VolatileConformanceFixture>
{
}

public sealed class VolatileRuntimeResultNormalizationConformanceTests : RuntimeStoreResultNormalizationConformanceTests<VolatileConformanceFixture>
{
}

public sealed class VolatileRuntimeEventConformanceTests : RuntimeStoreEventConformanceTests<VolatileConformanceFixture>
{
}
