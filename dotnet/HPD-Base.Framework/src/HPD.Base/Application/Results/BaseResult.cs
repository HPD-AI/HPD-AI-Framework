
namespace HPD.Base;

/// <summary>
/// Represents exactly one successful or failed BASE application outcome.
/// </summary>
/// <typeparam name="T">The successful value type.</typeparam>
public abstract record BaseResult<T>
{
    private protected BaseResult()
    {
    }

    /// <summary>
    /// Gets the canonical outcome status.
    /// </summary>
    public abstract OperationStatus Status { get; }

    /// <summary>
    /// Matches the successful or failed case.
    /// </summary>
    public abstract TResult Match<TResult>(
        Func<BaseSuccess<T>, TResult> success,
        Func<BaseFailure<T>, TResult> failure);

    /// <summary>
    /// Attempts to read the successful value.
    /// </summary>
    public bool TryGetValue(out T? value)
    {
        if (this is BaseSuccess<T> success)
        {
            value = success.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Returns the successful value or throws a bounded BASE application exception.
    /// </summary>
    public T RequireValue() =>
        this is BaseSuccess<T> success
            ? success.Value
            : throw BaseOperationException.From((BaseFailure<T>)this);
}

/// <summary>
/// Represents a successful BASE application outcome.
/// </summary>
public sealed record BaseSuccess<T> : BaseResult<T>
{
    internal BaseSuccess(
        T value,
        OperationStatus status,
        OperationWarning[]? warnings,
        RevisionInfo? revision,
        EventReference[]? events,
        OperationDiagnostics? diagnostics)
    {
        Value = value;
        Status = status;
        Warnings = warnings;
        Revision = revision;
        Events = events;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the successful value.</summary>
    public T Value { get; }

    /// <inheritdoc />
    public override OperationStatus Status { get; }

    /// <summary>Gets safe operation warnings.</summary>
    public OperationWarning[]? Warnings { get; }

    /// <summary>Gets revision metadata.</summary>
    public RevisionInfo? Revision { get; }

    /// <summary>Gets committed event references.</summary>
    public EventReference[]? Events { get; }

    /// <summary>Gets safe operation diagnostics.</summary>
    public OperationDiagnostics? Diagnostics { get; }

    /// <inheritdoc />
    public override TResult Match<TResult>(
        Func<BaseSuccess<T>, TResult> success,
        Func<BaseFailure<T>, TResult> failure)
    {
        ArgumentNullException.ThrowIfNull(success);
        ArgumentNullException.ThrowIfNull(failure);
        return success(this);
    }
}

/// <summary>
/// Represents a failed BASE application outcome.
/// </summary>
public sealed record BaseFailure<T> : BaseResult<T>
{
    internal BaseFailure(
        OperationStatus status,
        BaseError error,
        OperationWarning[]? warnings,
        OperationDiagnostics? diagnostics)
    {
        Status = status;
        Error = error;
        Warnings = warnings;
        Diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public override OperationStatus Status { get; }

    /// <summary>Gets the required safe error.</summary>
    public BaseError Error { get; }

    /// <summary>Gets safe operation warnings.</summary>
    public OperationWarning[]? Warnings { get; }

    /// <summary>Gets safe operation diagnostics.</summary>
    public OperationDiagnostics? Diagnostics { get; }

    /// <inheritdoc />
    public override TResult Match<TResult>(
        Func<BaseSuccess<T>, TResult> success,
        Func<BaseFailure<T>, TResult> failure)
    {
        ArgumentNullException.ThrowIfNull(success);
        ArgumentNullException.ThrowIfNull(failure);
        return failure(this);
    }
}
