using HPDOS.Core.Auth;

namespace HPDOS.Shell.Cli.TUI;

/// <summary>
/// Abstraction over provider connect/disconnect operations.
/// Local mode: wraps AuthManager directly (in-process).
/// Remote mode: wraps HttpClient calls to /api/providers on the remote server.
/// </summary>
public interface IProviderOperations
{
    Task<List<AuthSummary>> GetSummaryAsync();
    Task<List<AuthMethodInfo>> GetMethodsAsync(string providerId);
    Task<AuthFlowResult> StartLoginAsync(string providerId, int methodIndex, CancellationToken ct);
    Task<AuthFlowResult> CompleteLoginAsync(string providerId, int methodIndex, string input, CancellationToken ct);
    Task<bool> LogoutAsync(string providerId);
    Task<bool> LogoutEntryAsync(string providerId, string entryId);
    Task<bool> SetActiveEntryAsync(string providerId, string entryId);
}

/// <summary>
/// Lightweight DTO describing a single auth method for display in the TUI picker.
/// </summary>
public record AuthMethodInfo(string Label, string Description, bool IsRecommended);
