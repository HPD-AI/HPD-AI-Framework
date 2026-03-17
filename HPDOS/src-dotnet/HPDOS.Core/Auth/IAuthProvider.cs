namespace HPDOS.Core.Auth;

public interface IAuthProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    IReadOnlyList<AuthMethod> Methods { get; }
    IReadOnlyList<string> EnvironmentVariables { get; }

    Task<AuthLoadResult> LoadAsync(AuthEntry entry);
    Task<AuthEntry?> RefreshIfNeededAsync(AuthEntry entry);
    Task<bool> ValidateAsync(AuthEntry entry);
}

public record AuthMethod
{
    public required AuthType Type { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public bool IsRecommended { get; init; }
    public required Func<CancellationToken, Task<AuthFlowResult>> StartFlow { get; init; }
}

public enum AuthType
{
    OAuthBrowser,
    OAuthDeviceCode,
    OAuthManualCode,
    ApiKey,
    WellKnown
}

public abstract record AuthFlowResult
{
    public sealed record Success(AuthEntry Entry) : AuthFlowResult;
    public sealed record Cancelled : AuthFlowResult;
    public sealed record Failed(string Error, Exception? Exception = null) : AuthFlowResult;
    public sealed record PendingUserAction(
        string Message,
        string? Url,
        string? UserCode,
        Func<CancellationToken, Task<AuthFlowResult>> WaitForCompletion
    ) : AuthFlowResult;
    public sealed record NeedsUserInput(
        string Prompt,
        string InputLabel,
        Func<string, CancellationToken, Task<AuthFlowResult>> CompleteWithInput
    ) : AuthFlowResult;
}

public record AuthLoadResult
{
    public required string ApiKey { get; init; }
    public Dictionary<string, string>? CustomHeaders { get; init; }
    public string? BaseUrl { get; init; }
    public string? AccountId { get; init; }
}
