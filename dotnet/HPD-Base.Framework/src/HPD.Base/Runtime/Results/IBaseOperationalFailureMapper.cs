
namespace HPD.Base;

public interface IBaseOperationalFailureMapper
{
    bool TryMap(Exception exception, OperationContext operation, out BaseError error, out OperationStatus status);
}
