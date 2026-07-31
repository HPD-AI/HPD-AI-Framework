using System.Reflection;
using FluentAssertions;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Contracts;

public sealed class PublicApiShapeTests
{
    [Fact]
    public void RuntimeFacadeExposesOnlyInProcessRuntimeServices()
    {
        typeof(IHPDBaseRuntime)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.PropertyType)
            .Should()
            .BeEquivalentTo([
                typeof(IBaseDescriptorProvider),
                typeof(IBaseSchemaProvider),
                typeof(IBaseCapabilityProvider),
                typeof(IBaseRecordRuntime),
                typeof(IBaseHealthProvider),
                typeof(IBaseDiagnosticProvider),
                typeof(IBaseJsonOptionsProvider)
            ]);

        typeof(IHPDBaseRuntime)
            .GetMethod(nameof(IHPDBaseRuntime.ValidateAsync))
            .Should()
            .NotBeNull()
            .And
            .Match<MethodInfo>(method => method.ReturnType == typeof(ValueTask<BaseRuntimeValidationResult>));
    }

    [Fact]
    public void BuilderAndOptionsMatchL5ContractShape()
    {
        typeof(IHPDBaseRuntimeBuilder)
            .GetProperty(nameof(IHPDBaseRuntimeBuilder.Services))
            .Should()
            .NotBeNull()
            .And
            .Match<PropertyInfo>(property => property.PropertyType == typeof(IServiceCollection));

        typeof(IHPDBaseRuntimeBuilder)
            .GetProperty(nameof(IHPDBaseRuntimeBuilder.Options))
            .Should()
            .NotBeNull()
            .And
            .Match<PropertyInfo>(property => property.PropertyType == typeof(HPDBaseRuntimeOptions));

        typeof(HPDBaseRuntimeOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Should()
            .BeEquivalentTo([
                nameof(HPDBaseRuntimeOptions.Runtime),
                nameof(HPDBaseRuntimeOptions.Compatibility),
                nameof(HPDBaseRuntimeOptions.ManifestVersion),
                nameof(HPDBaseRuntimeOptions.DefaultManifestVisibility),
                nameof(HPDBaseRuntimeOptions.FailFastOnDescriptorValidation),
                nameof(HPDBaseRuntimeOptions.Limits),
                nameof(HPDBaseRuntimeOptions.Events),
                nameof(HPDBaseRuntimeOptions.Mutations),
                nameof(HPDBaseRuntimeOptions.Redaction),
                nameof(HPDBaseRuntimeOptions.Observability)
            ]);
    }

    [Fact]
    public void DescriptorContributionBuilderExposesOnlyExplicitContributionMethods()
    {
        typeof(IBaseDescriptorContributionBuilder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .Should()
            .BeEquivalentTo([
                nameof(IBaseDescriptorContributionBuilder.AddModule),
                nameof(IBaseDescriptorContributionBuilder.AddProjection),
                nameof(IBaseDescriptorContributionBuilder.AddDtoContract),
                nameof(IBaseDescriptorContributionBuilder.AddEventType),
                nameof(IBaseDescriptorContributionBuilder.AddHealthRef),
                nameof(IBaseDescriptorContributionBuilder.AddDiagnosticRef),
                nameof(IBaseDescriptorContributionBuilder.AddFieldAnnotation),
                nameof(IBaseDescriptorContributionBuilder.AddSchema),
                nameof(IBaseDescriptorContributionBuilder.AddCollection),
                nameof(IBaseDescriptorContributionBuilder.AddCapabilities),
                nameof(IBaseDescriptorContributionBuilder.AddHealth),
                nameof(IBaseDescriptorContributionBuilder.AddDiagnostic)
            ]);
    }

    [Fact]
    public void RecordRuntimeMethodsReturnOperationResultsAndAcceptCancellation()
    {
        var methods = typeof(IBaseRecordRuntime)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(method => method.MetadataToken)
            .ToArray();

        methods.Select(method => method.Name)
            .Should()
            .Equal([
                nameof(IBaseRecordRuntime.ListAsync),
                nameof(IBaseRecordRuntime.GetAsync),
                nameof(IBaseRecordRuntime.CreateAsync),
                nameof(IBaseRecordRuntime.PatchAsync),
                nameof(IBaseRecordRuntime.ReplaceAsync),
                nameof(IBaseRecordRuntime.DeleteAsync),
                nameof(IBaseRecordRuntime.UpsertAsync),
                nameof(IBaseRecordRuntime.BatchAsync)
            ]);

        methods.Should().AllSatisfy(method =>
            method.GetParameters().Last().ParameterType.Should().Be(typeof(CancellationToken)));

        methods.Select(method => method.ReturnType).Should().Equal([
            typeof(ValueTask<OperationResult<RecordPage>>),
            typeof(ValueTask<OperationResult<RecordEnvelope>>),
            typeof(ValueTask<OperationResult<RecordEnvelope>>),
            typeof(ValueTask<OperationResult<RecordEnvelope>>),
            typeof(ValueTask<OperationResult<RecordEnvelope>>),
            typeof(ValueTask<OperationResult<DeleteResult>>),
            typeof(ValueTask<OperationResult<RecordUpsertResult>>),
            typeof(ValueTask<OperationResult<BaseRecordBatchResult>>)
        ]);
    }

    [Fact]
    public void RuntimeLocalDtosDoNotLeakIntoAbstractionsAssembly()
    {
        var runtimeDtoTypes = new[]
        {
            typeof(BaseRuntimeValidationResult),
            typeof(BaseRuntimeValidationIssue),
            typeof(BaseManifestRequest),
            typeof(BaseManifestExpansionRequest),
            typeof(ExpandedBaseManifest),
            typeof(BasePayloadValidationRequest),
            typeof(BaseValidatedPayload),
            typeof(ValidatedRecordQuery),
            typeof(BasePolicyRequest),
            typeof(BasePolicyEvaluation),
            typeof(RecordStoreRegistration)
        };

        foreach (var runtimeDtoType in runtimeDtoTypes)
        {
            Assert.Same(typeof(IHPDBaseRuntime).Assembly, runtimeDtoType.Assembly);
        }
    }

    [Fact]
    public void RuntimeResultsAndEventsExposeL5Helpers()
    {
        typeof(OperationResults).GetMethod(nameof(OperationResults.Ok)).Should().NotBeNull();
        typeof(OperationResults).GetMethod(nameof(OperationResults.Created)).Should().NotBeNull();
        typeof(OperationResults).GetMethod(nameof(OperationResults.Updated)).Should().NotBeNull();
        typeof(OperationResults).GetMethod(nameof(OperationResults.Deleted)).Should().NotBeNull();
        typeof(OperationResults).GetMethod(nameof(OperationResults.NoContent)).Should().NotBeNull();
        typeof(OperationResults).GetMethod(nameof(OperationResults.StoreError)).Should().NotBeNull();

        typeof(IBaseEventFactory)
            .GetMethod(nameof(IBaseEventFactory.CreateRecordMutationEvent))
            .Should()
            .NotBeNull()
            .And
            .Match<MethodInfo>(method => method.ReturnType == typeof(BaseRecordMutationEvent));
    }
}
