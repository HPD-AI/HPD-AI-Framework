
namespace HPD.Base;

/// <summary>Defines the ibase result normalizer contract.</summary>
public interface IBaseResultNormalizer
{
    /// <summary>Executes the normalize store result operation.</summary>
    OperationResult<T> NormalizeStoreResult<T>(
        OperationResult<T> result,
        OperationContext operation);
}
