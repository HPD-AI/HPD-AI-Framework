namespace HPD.Base;

/// <summary>Identifies a dependency invalidation that could not be represented safely.</summary>
public sealed class BaseDependencyInvalidationException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    public BaseDependencyInvalidationException(string safeMessage)
        : base(safeMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        SafeMessage = safeMessage;
    }

    /// <summary>Gets a bounded message that contains no resolved dependency values.</summary>
    public string SafeMessage { get; }
}
