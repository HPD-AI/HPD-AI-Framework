namespace HPD.TUI.Flows;

public readonly record struct PromptResult<T>(PromptResultStatus Status, T? Value, string? Error = null)
{
    public bool IsSubmitted => Status == PromptResultStatus.Submitted;

    public bool IsCanceled => Status == PromptResultStatus.Canceled;

    public static PromptResult<T> Submitted(T value) => new(PromptResultStatus.Submitted, value);

    public static PromptResult<T> Canceled() => new(PromptResultStatus.Canceled, default);

    public static PromptResult<T> Failed(string error) => new(PromptResultStatus.Failed, default, error);
}

public enum PromptResultStatus
{
    Submitted,
    Canceled,
    Failed
}
