using HPD.Base.Results;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Results;
using HPD.Base.Schema;

namespace HPD.Base.Runtime.Schema;

internal sealed class DefaultBaseSchemaProvider : IBaseSchemaProvider
{
    private readonly IBaseDescriptorRegistry _registry;

    public DefaultBaseSchemaProvider(IBaseDescriptorRegistry registry)
    {
        _registry = registry;
    }

    public ValueTask<OperationResult<SchemaMetadata>> GetSchemaAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = principal;
        _ = operation;
        return ValueTask.FromResult(OperationResults.Ok(DescriptorViewFilter.Schema(_registry.Current, view)));
    }

    public ValueTask<OperationResult<CollectionDefinition>> GetCollectionAsync(
        string collectionId,
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = principal;
        _ = operation;
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
    }
}
