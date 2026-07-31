namespace HPD.Base;

public sealed record BaseRuntimeValidationResult
{
    public required bool Succeeded { get; init; }
    public BaseRuntimeValidationIssue[]? Issues { get; init; }

    public static BaseRuntimeValidationResult Success { get; } = new() { Succeeded = true };
}
