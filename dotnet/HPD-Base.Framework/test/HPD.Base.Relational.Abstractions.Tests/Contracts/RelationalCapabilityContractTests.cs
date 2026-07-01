using System.Text.Json;
using HPD.Base.Descriptors;
using HPD.Base.Relational;
using HPD.Base.Relational.Abstractions.Tests.Serialization;
using HPD.Base.Relational.Serialization;

namespace HPD.Base.Relational.Abstractions.Tests.Contracts;

public sealed class RelationalCapabilityContractTests
{
    [Fact]
    public void ConstantsAreStableAndModuleOwned()
    {
        Assert.Equal("hpd.base.relational.abstractions", RelationalModuleIds.RelationalAbstractions);
        Assert.Equal("relational", RelationalCapabilityFamilies.Relational);
        Assert.Equal("relational.metadata.read", RelationalFeatureIds.MetadataRead);
        Assert.Equal("relational.mapping.collections.read", RelationalFeatureIds.CollectionMappingRead);
        Assert.Equal("relational.query.plan.explain", RelationalFeatureIds.QueryPlanExplain);
        Assert.Equal("relational.schemaWrite", RelationalFeatureIds.SchemaWrite);
        Assert.Equal("relational.transactions", RelationalFeatureIds.Transactions);
    }

    [Fact]
    public void DescriptorCanStateCapabilityTruthWithoutCallableMutationSurface()
    {
        var descriptor = RelationalSamples.CapabilityDescriptor();
        var json = JsonSerializer.Serialize(descriptor, HPDBaseRelationalJsonSerializerContext.Default.RelationalCapabilityDescriptor);

        Assert.Contains("\"status\":\"planned\"", json);
        Assert.Contains("\"callableInterfaceAvailable\":false", json);
        Assert.Contains("\"callableIncludeExecutionAvailable\":false", json);
        Assert.Contains("\"definitionChangeRunnerAvailable\":false", json);
        Assert.Equal(CapabilityStatus.Planned, descriptor.Transactions!.Status);
        Assert.Equal(CapabilityStatus.Planned, descriptor.JoinsIncludes!.Status);
        Assert.False(descriptor.JoinsIncludes.CallableIncludeExecutionAvailable);
        Assert.False(descriptor.Transactions.CallableInterfaceAvailable);
        Assert.False(descriptor.SchemaWrite!.CallableInterfaceAvailable);
    }
}
