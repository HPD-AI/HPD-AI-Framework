using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Serialization;

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
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.OperationResultRecordUpsertResult);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.OperationResultBaseRecordBatchResult);
    }

    [Fact]
    public void IncludesRepresentativeKernelDtos()
    {
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.BaseManifest);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.SchemaMetadata);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.RecordEnvelope);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.BaseRecordMutationEvent);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.HealthDescriptor);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.DiagnosticDescriptor);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.BaseError);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.BaseRecordBatchRequest);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.BaseRecordBatchResult);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.RecordUpsertRequest);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.RecordMutationExecutionResult);
        Assert.NotNull(HPDBaseJsonSerializerContext.Default.BaseRecordMutationFact);
    }
}
