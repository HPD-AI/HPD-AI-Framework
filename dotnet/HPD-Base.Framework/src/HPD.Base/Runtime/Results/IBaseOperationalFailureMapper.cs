
namespace HPD.Base;

/// <summary>Defines the ibase operational failure mapper contract.</summary>
public interface IBaseOperationalFailureMapper
{
    /// <summary>Executes the try map operation.</summary>
    bool TryMap(Exception exception, OperationContext operation, out BaseError error, out OperationStatus status);
}
