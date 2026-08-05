
namespace HPD.Base;

/// <summary>Defines the ibase schema provider contract.</summary>
public interface IBaseSchemaProvider
{
    /// <summary>Executes the get schema async operation.</summary>
    ValueTask<OperationResult<SchemaMetadata>> GetSchemaAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the get collection async operation.</summary>
    ValueTask<OperationResult<CollectionDefinition>> GetCollectionAsync(
        string collectionId,
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default);
}
