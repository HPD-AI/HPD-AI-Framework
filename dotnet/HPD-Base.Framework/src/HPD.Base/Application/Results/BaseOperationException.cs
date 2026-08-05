
namespace HPD.Base;

/// <summary>
/// Represents an explicitly requested exception projection of a BASE failure.
/// </summary>
public sealed class BaseOperationException : Exception
{
    private BaseOperationException(
        OperationStatus status,
        BaseError error)
        : base(error.Message)
    {
        Status = status;
        Error = error;
    }

    /// <summary>Gets the canonical failure status.</summary>
    public OperationStatus Status { get; }

    /// <summary>Gets the safe BASE error.</summary>
    public BaseError Error { get; }

    internal static BaseOperationException From<T>(BaseFailure<T> failure) =>
        new(failure.Status, failure.Error);
}
