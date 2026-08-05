
namespace HPD.Base;

/// <summary>
/// Reports which atomic upsert branch committed and its typed record.
/// </summary>
public sealed record BaseUpsertResult<T>
{
    /// <summary>Gets the committed upsert branch.</summary>
    public required RecordUpsertOutcome Outcome { get; init; }

    /// <summary>Gets the committed typed record.</summary>
    public required BaseRecord<T> Record { get; init; }
}

/// <summary>
/// Reports whether ensure created a record or observed an existing record.
/// </summary>
public enum BaseEnsureOutcome
{
    /// <summary>The record was created.</summary>
Created,

    /// <summary>An existing record was read without being modified.</summary>
AlreadyExisted,
}

/// <summary>
/// Represents an honest ensure result.
/// </summary>
public sealed record BaseEnsureResult<T>
{
    /// <summary>Gets the ensure outcome.</summary>
    public required BaseEnsureOutcome Outcome { get; init; }

    /// <summary>Gets the created or existing typed record.</summary>
    public required BaseRecord<T> Record { get; init; }
}
