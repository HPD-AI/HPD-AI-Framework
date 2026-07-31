
namespace HPD.Base;

public interface IRelationalMetadataProvider
{
    ValueTask<OperationResult<RelationalStoreDescriptor>> GetStoreAsync(
        OperationContext context,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<RelationalTableDescriptor[]>> ListTablesAsync(
        OperationContext context,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<RelationalViewDescriptor[]>> ListViewsAsync(
        OperationContext context,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default);
}

public interface IRelationalCollectionMappingProvider
{
    ValueTask<OperationResult<RelationalCollectionMappingDescriptor?>> GetMappingAsync(
        CollectionDefinition collection,
        OperationContext context,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<RelationalCollectionMappingDescriptor[]>> ListMappingsAsync(
        OperationContext context,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default);
}

public interface IRelationalQueryPlanExplainer
{
    ValueTask<OperationResult<RelationalQueryPlanDescriptor>> ExplainAsync(
        CollectionDefinition collection,
        OperationContext context,
        RecordQuery query,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default);
}
