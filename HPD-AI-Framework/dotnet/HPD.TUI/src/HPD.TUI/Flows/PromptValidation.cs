namespace HPD.TUI.Flows;

public readonly record struct PromptValidationResult(bool IsValid, string? Message = null)
{
    public static PromptValidationResult Valid { get; } = new(true);

    public static PromptValidationResult Invalid(string message) => new(false, message);
}

public delegate PromptValidationResult PromptValidator<in T>(T value);
