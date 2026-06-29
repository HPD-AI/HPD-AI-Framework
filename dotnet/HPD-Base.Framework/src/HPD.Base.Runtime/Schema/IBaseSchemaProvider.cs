using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;

namespace HPD.Base.Runtime.Schema;

public interface IBaseSchemaProvider
{
    ValueTask<OperationResult<SchemaMetadata>> GetSchemaAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<CollectionDefinition>> GetCollectionAsync(
        string collectionId,
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default);
}
