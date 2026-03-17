using HPDOS.Core.Auth;

namespace HPDOS.Shell.Cli.TUI;

/// <summary>
/// Local mode: calls AuthManager directly in-process.
/// </summary>
public class LocalProviderOperations(AuthManager authManager) : IProviderOperations
{
    public Task<List<AuthSummary>> GetSummaryAsync() =>
        authManager.GetAuthSummaryAsync();

    public Task<List<AuthMethodInfo>> GetMethodsAsync(string providerId)
    {
        var provider = authManager.GetProvider(providerId);
        if (provider is null)
            return Task.FromResult(new List<AuthMethodInfo>());

        var methods = provider.Methods
            .Select(m => new AuthMethodInfo(m.Label, m.Description ?? string.Empty, m.IsRecommended))
            .ToList();

        return Task.FromResult(methods);
    }

    public async Task<AuthFlowResult> StartLoginAsync(string providerId, int methodIndex, CancellationToken ct)
    {
        var provider = authManager.GetProvider(providerId);
        if (provider is null)
            return new AuthFlowResult.Failed($"Unknown provider: {providerId}");

        if (methodIndex < 0 || methodIndex >= provider.Methods.Count)
            return new AuthFlowResult.Failed($"Method index {methodIndex} out of range");

        var method = provider.Methods[methodIndex];
        var result = await method.StartFlow(ct);

        return result switch
        {
            // Persist immediately on success (e.g. OAuthBrowser, WellKnown env-var flows).
            AuthFlowResult.Success success => await PersistAsync(providerId, method.Label, success),
            // Wrap PendingUserAction so the final success is also persisted (device code flows).
            AuthFlowResult.PendingUserAction pending => new AuthFlowResult.PendingUserAction(
                pending.Message, pending.Url, pending.UserCode,
                async innerCt =>
                {
                    var inner = await pending.WaitForCompletion(innerCt);
                    if (inner is AuthFlowResult.Success innerSuccess)
                        await authManager.Storage.SetAsync(providerId, innerSuccess.Entry with { MethodLabel = method.Label });
                    return inner;
                }),
            _ => result
        };
    }

    public async Task<AuthFlowResult> CompleteLoginAsync(string providerId, int methodIndex, string input, CancellationToken ct)
    {
        var provider = authManager.GetProvider(providerId);
        if (provider is null)
            return new AuthFlowResult.Failed($"Unknown provider: {providerId}");

        if (methodIndex < 0 || methodIndex >= provider.Methods.Count)
            return new AuthFlowResult.Failed($"Method index {methodIndex} out of range");

        var method = provider.Methods[methodIndex];
        var startResult = await method.StartFlow(ct);
        if (startResult is not AuthFlowResult.NeedsUserInput needsInput)
            return new AuthFlowResult.Failed("Flow does not require input at this stage");

        var result = await needsInput.CompleteWithInput(input, ct);
        if (result is AuthFlowResult.Success success)
            return await PersistAsync(providerId, method.Label, success);

        return result;
    }

    private async Task<AuthFlowResult> PersistAsync(string providerId, string methodLabel, AuthFlowResult.Success success)
    {
        await authManager.Storage.SetAsync(providerId, success.Entry with { MethodLabel = methodLabel });
        return success;
    }

    public async Task<bool> LogoutAsync(string providerId) =>
        await authManager.Storage.RemoveAsync(providerId);

    public async Task<bool> LogoutEntryAsync(string providerId, string entryId) =>
        await authManager.Storage.RemoveEntryAsync(providerId, entryId);

    public async Task<bool> SetActiveEntryAsync(string providerId, string entryId) =>
        await authManager.Storage.SetActiveAsync(providerId, entryId);
}
