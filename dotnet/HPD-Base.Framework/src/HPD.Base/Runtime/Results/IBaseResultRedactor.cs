using HPD.Base.Results;

namespace HPD.Base.Runtime.Results;

public interface IBaseResultRedactor
{
    OperationResult<T> Redact<T>(OperationResult<T> result, VisibilityLevel view);
    OperationResult Redact(OperationResult result, VisibilityLevel view);
}
