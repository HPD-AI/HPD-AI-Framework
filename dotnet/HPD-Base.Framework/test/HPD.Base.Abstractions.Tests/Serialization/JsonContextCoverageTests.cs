using HPD.Base.Descriptors;
using HPD.Base.Events;
using HPD.Base.Health;
using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Schema;
using HPD.Base.Serialization;

namespace HPD.Base.Abstractions.Tests.Serialization;

public sealed class JsonContextCoverageTests
{
    [Fact]
    public void IncludesClosedGenericResultForms()
    {
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.OperationResultRecordPage);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.OperationResultRecordEnvelope);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.OperationResultDeleteResult);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.OperationResultEventPublishResult);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.OperationResultBaseManifest);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.OperationResultCapabilityDescriptor);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.OperationResultSchemaMetadata);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.OperationResultPolicyDecision);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.OperationResultHealthDescriptorArray);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.OperationResultDiagnosticDescriptorArray);
    }

    [Fact]
    public void IncludesRepresentativeKernelDtos()
    {
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.BaseManifest);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.SchemaMetadata);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.RecordEnvelope);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.BaseEventEnvelope);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.HealthDescriptor);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.DiagnosticDescriptor);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.BaseError);
    }
}
