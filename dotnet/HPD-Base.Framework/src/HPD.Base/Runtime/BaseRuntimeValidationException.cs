namespace HPD.Base;

/// <summary>Represents a base runtime validation exception.</summary>
public sealed class BaseRuntimeValidationException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    public BaseRuntimeValidationException(BaseRuntimeValidationResult validation)
        : base("HPD.BASE Runtime validation failed.")
    {
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
    }

    /// <summary>Initializes a new instance.</summary>
    public BaseRuntimeValidationException(string message, BaseRuntimeValidationResult validation)
        : base(message)
    {
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
    }

    /// <summary>Gets the validation.</summary>
    public BaseRuntimeValidationResult Validation { get; }
}
