
namespace HPD.Base;

/// <summary>Defines the ibase result redactor contract.</summary>
public interface IBaseResultRedactor
{
    /// <summary>Executes the redact operation.</summary>
    OperationResult<T> Redact<T>(OperationResult<T> result, VisibilityLevel view);
    /// <summary>Executes the redact operation.</summary>
    OperationResult Redact(OperationResult result, VisibilityLevel view);
}
