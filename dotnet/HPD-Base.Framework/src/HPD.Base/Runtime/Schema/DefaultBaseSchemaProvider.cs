
namespace HPD.Base;

internal sealed class DefaultBaseSchemaProvider : IBaseSchemaProvider
{
    private readonly IBaseDescriptorRegistry _registry;

    /// <summary>Initializes a new instance.</summary>
    public DefaultBaseSchemaProvider(IBaseDescriptorRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Executes the get schema async operation.</summary>
    public ValueTask<OperationResult<SchemaMetadata>> GetSchemaAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = principal;
        return HPDBaseRuntimeTelemetry.TraceRuntimeReadAsync(
            HPDBaseTelemetrySpans.RuntimeSchemaGet,
            BaseOperationKind.SchemaRead,
            operation.CollectionId,
            view,
            !string.IsNullOrWhiteSpace(operation.CorrelationId),
            countAsHealthRead: false,
            countAsDiagnosticRead: false,
            () => ValueTask.FromResult(OperationResults.Ok(DescriptorViewFilter.Schema(_registry.Current, view))));
    }

    /// <summary>Executes the get collection async operation.</summary>
    public ValueTask<OperationResult<CollectionDefinition>> GetCollectionAsync(
        string collectionId,
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = principal;
        return HPDBaseRuntimeTelemetry.TraceRuntimeReadAsync(
            HPDBaseTelemetrySpans.RuntimeSchemaCollectionGet,
            BaseOperationKind.SchemaRead,
            collectionId,
            view,
            !string.IsNullOrWhiteSpace(operation.CorrelationId),
            countAsHealthRead: false,
            countAsDiagnosticRead: false,
            () =>
            {
                var schema = DescriptorViewFilter.Schema(_registry.Current, view);

                var collection = schema.Collections?.FirstOrDefault(
                    item => string.Equals(item.Id, collectionId, StringComparison.Ordinal));

                return ValueTask.FromResult(collection is null
                    ? OperationResults.NotFound<CollectionDefinition>(new BaseError
                    {
                        Code = "base.runtime.collection.notFound",
                        Message = "Collection was not found.",
                        Category = ErrorCategory.NotFound,
                        Target = collectionId
                    })
                    : OperationResults.Ok(collection));
            });
    }
}
