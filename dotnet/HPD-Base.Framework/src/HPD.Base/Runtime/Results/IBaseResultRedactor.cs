
namespace HPD.Base;

public interface IBaseResultRedactor
{
    OperationResult<T> Redact<T>(OperationResult<T> result, VisibilityLevel view);
    OperationResult Redact(OperationResult result, VisibilityLevel view);
}
