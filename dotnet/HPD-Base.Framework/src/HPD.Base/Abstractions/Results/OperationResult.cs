namespace HPD.Base;

/// <summary>Represents a operation result.</summary>
public sealed record OperationResult<T>
{
    /// <summary>Gets or sets the status.</summary>
    public required OperationStatus Status { get; init; }
    /// <summary>Gets or sets the value.</summary>
    public T? Value { get; init; }
    /// <summary>Gets or sets the error.</summary>
    public BaseError? Error { get; init; }
    /// <summary>Gets or sets the warnings.</summary>
    public OperationWarning[]? Warnings { get; init; }
    /// <summary>Gets or sets the diagnostics.</summary>
    public OperationDiagnostics? Diagnostics { get; init; }
    /// <summary>Gets or sets the revision.</summary>
    public RevisionInfo? Revision { get; init; }
    /// <summary>Gets or sets the events.</summary>
    public EventReference[]? Events { get; init; }
}

/// <summary>Represents a operation result.</summary>
public sealed record OperationResult
{
    /// <summary>Gets or sets the status.</summary>
    public required OperationStatus Status { get; init; }
    /// <summary>Gets or sets the error.</summary>
    public BaseError? Error { get; init; }
    /// <summary>Gets or sets the warnings.</summary>
    public OperationWarning[]? Warnings { get; init; }
    /// <summary>Gets or sets the diagnostics.</summary>
    public OperationDiagnostics? Diagnostics { get; init; }
    /// <summary>Gets or sets the revision.</summary>
    public RevisionInfo? Revision { get; init; }
    /// <summary>Gets or sets the events.</summary>
    public EventReference[]? Events { get; init; }
}
