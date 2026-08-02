
namespace HPD.Base;

/// <summary>Defines the irelational metadata provider contract.</summary>
public interface IRelationalMetadataProvider
{
    /// <summary>Executes the get store async operation.</summary>
    ValueTask<OperationResult<RelationalStoreDescriptor>> GetStoreAsync(
        OperationContext context,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the list tables async operation.</summary>
    ValueTask<OperationResult<RelationalTableDescriptor[]>> ListTablesAsync(
        OperationContext context,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the list views async operation.</summary>
    ValueTask<OperationResult<RelationalViewDescriptor[]>> ListViewsAsync(
        OperationContext context,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default);
}

/// <summary>Defines the irelational collection mapping provider contract.</summary>
public interface IRelationalCollectionMappingProvider
{
    /// <summary>Executes the get mapping async operation.</summary>
    ValueTask<OperationResult<RelationalCollectionMappingDescriptor?>> GetMappingAsync(
        CollectionDefinition collection,
        OperationContext context,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the list mappings async operation.</summary>
    ValueTask<OperationResult<RelationalCollectionMappingDescriptor[]>> ListMappingsAsync(
        OperationContext context,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default);
}

/// <summary>Defines the irelational query plan explainer contract.</summary>
public interface IRelationalQueryPlanExplainer
{
    /// <summary>Executes the explain async operation.</summary>
    ValueTask<OperationResult<RelationalQueryPlanDescriptor>> ExplainAsync(
        CollectionDefinition collection,
        OperationContext context,
        RecordQuery query,
        VisibilityLevel visibility,
        CancellationToken cancellationToken = default);
}
