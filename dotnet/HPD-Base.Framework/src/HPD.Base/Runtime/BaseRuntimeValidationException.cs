namespace HPD.Base.Runtime;

public sealed class BaseRuntimeValidationException : Exception
{
    public BaseRuntimeValidationException(BaseRuntimeValidationResult validation)
        : base("HPD.BASE Runtime validation failed.")
    {
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
    }

    public BaseRuntimeValidationException(string message, BaseRuntimeValidationResult validation)
        : base(message)
    {
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
    }

    public BaseRuntimeValidationResult Validation { get; }
}
