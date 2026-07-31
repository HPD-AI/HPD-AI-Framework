namespace HPD.Base.Results;

public sealed record OperationResult<T>
{
    public required OperationStatus Status { get; init; }
    public T? Value { get; init; }
    public BaseError? Error { get; init; }
    public OperationWarning[]? Warnings { get; init; }
    public OperationDiagnostics? Diagnostics { get; init; }
    public RevisionInfo? Revision { get; init; }
    public EventReference[]? Events { get; init; }
}

public sealed record OperationResult
{
    public required OperationStatus Status { get; init; }
    public BaseError? Error { get; init; }
    public OperationWarning[]? Warnings { get; init; }
    public OperationDiagnostics? Diagnostics { get; init; }
    public RevisionInfo? Revision { get; init; }
    public EventReference[]? Events { get; init; }
}
