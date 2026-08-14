using HPD.Payments.Primitives.Classification;

namespace HPD.Payments.Primitives.Results;

/// <summary>Represents the bounded immutable <c>ResultKind</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public enum ResultKind
{
    /// <summary>Invalid default result.</summary>
    None = 0,
    /// <summary>The named operation completed with a value.</summary>
    Success,
    /// <summary>The named operation failed with a bounded code.</summary>
    Failure,
    /// <summary>Whether an external occurrence happened is unknown.</summary>
    Unknown,
    /// <summary>A decision is blocked because required facts are unavailable or incomparable.</summary>
    Indeterminate,
    /// <summary>Local processing terminated while typed consequences remain.</summary>
    Residual,
    /// <summary>Available evidence cannot verify the historical or external claim.</summary>
    Unverifiable,
    /// <summary>Presented facts disagree with an existing binding or decision.</summary>
    Conflict,
    /// <summary>The valid variant or version is not supported.</summary>
    Unsupported,
    /// <summary>Success depends on an explicit condition that remains visible.</summary>
    Conditional,
    /// <summary>The applicable named time boundary has elapsed.</summary>
    Expired,
    /// <summary>The requested activity was cancelled without implying external non-occurrence.</summary>
    Cancelled,
    /// <summary>A typed successor replaces this value for the applicable read frame.</summary>
    Superseded
}

/// <summary>Represents either a typed success or one exact non-success/uncertainty variant with a bounded code and optional owned evidence.</summary>
/// <typeparam name="T">The non-null success value type.</typeparam>
public sealed class PrimitiveResult<T> where T : notnull
{
    /// <summary>Maximum UTF-8 byte length of a stable non-success code.</summary>
    public const int MaximumCodeUtf8Bytes = 128;
    /// <summary>Gets the validated <c>Kind</c> component; it does not imply ambient context or mutation authority.</summary>
    public ResultKind Kind { get; }
    /// <summary>Gets the validated <c>Value</c> component; it does not imply ambient context or mutation authority.</summary>
    public T? Value { get; }
    /// <summary>Gets the validated <c>Code</c> component; it does not imply ambient context or mutation authority.</summary>
    public string Code { get; }
    /// <summary>Gets the validated <c>Evidence</c> component; it does not imply ambient context or mutation authority.</summary>
    public OwnedClassifiedBytes? Evidence { get; }
    /// <summary>Gets whether this result is the <see cref="ResultKind.Success"/> variant.</summary>
    public bool IsSuccess => Kind == ResultKind.Success;

    internal PrimitiveResult(ResultKind kind, T? value, string code, OwnedClassifiedBytes? evidence) => (Kind, Value, Code, Evidence) = (kind, value, code, evidence);

    /// <summary>Exhaustively handles success and non-success without default fallthrough.</summary>
    /// <typeparam name="TResult">The common visitor result type.</typeparam>
    /// <param name="success">Called only for <see cref="ResultKind.Success"/> with the non-null value.</param>
    /// <param name="nonSuccess">Called for every non-success kind with its code and owned evidence.</param>
    /// <returns>The value returned by the selected delegate.</returns>
    /// <exception cref="ArgumentNullException">Either delegate is <see langword="null"/>.</exception>
    public TResult Match<TResult>(Func<T, TResult> success, Func<ResultKind, string, OwnedClassifiedBytes?, TResult> nonSuccess)
    {
        ArgumentNullException.ThrowIfNull(success); ArgumentNullException.ThrowIfNull(nonSuccess);
        return IsSuccess ? success(Value!) : nonSuccess(Kind, Code, Evidence);
    }
}

/// <summary>Creates validated typed result variants; it owns no authority state.</summary>
public static class PrimitiveResults
{
    /// <summary>Creates a success result containing a non-null value.</summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="value">The non-null success value.</param>
    /// <returns>A success result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static PrimitiveResult<T> Success<T>(T value) where T : notnull => new(ResultKind.Success, value ?? throw new ArgumentNullException(nameof(value)), "success", null);
    /// <summary>Creates one explicit non-success variant; unknown and indeterminate remain distinct.</summary>
    /// <typeparam name="T">The absent success value type.</typeparam>
    /// <param name="kind">A known kind other than <see cref="ResultKind.None"/> or <see cref="ResultKind.Success"/>.</param>
    /// <param name="code">A non-empty lowercase ASCII code of at most 128 UTF-8 bytes.</param>
    /// <param name="evidence">Optional already-owned classified evidence.</param>
    /// <returns>A non-success result with no success value.</returns>
    /// <exception cref="ArgumentException">The kind or code is invalid.</exception>
    public static PrimitiveResult<T> NonSuccess<T>(ResultKind kind, string code, OwnedClassifiedBytes? evidence = null) where T : notnull
    {
        if (kind is ResultKind.None or ResultKind.Success || !Enum.IsDefined(kind) || !Identity.ScopeId.TryComponent(code, out var stable)) throw new ArgumentException("A non-success result requires a known kind and bounded stable code.");
        return new(kind, default, stable, evidence);
    }
}
