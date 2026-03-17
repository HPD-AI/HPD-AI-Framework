using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPDOS.Core.Auth;

namespace HPDOS.Shell.Cli.TUI;

/// <summary>
/// Remote mode: calls /api/providers endpoints on the remote HPDOS server.
/// </summary>
public class RemoteProviderOperations(HttpClient http) : IProviderOperations
{
    public async Task<List<AuthSummary>> GetSummaryAsync()
    {
        var result = await http.GetFromJsonAsync<List<AuthSummary>>("/api/providers", HpdosJsonOptions.Http);
        return result ?? [];
    }

    public async Task<List<AuthMethodInfo>> GetMethodsAsync(string providerId)
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<AuthMethodInfoDto>>(
                $"/api/providers/{providerId}/methods", HpdosJsonOptions.Http);
            if (result is { Count: > 0 })
                return result.Select(m => new AuthMethodInfo(m.Label, m.Description ?? string.Empty, m.IsRecommended)).ToList();
        }
        catch { /* fall through to fallback */ }

        return [new AuthMethodInfo("Connect", string.Empty, IsRecommended: true)];
    }

    public async Task<AuthFlowResult> StartLoginAsync(string providerId, int methodIndex, CancellationToken ct)
    {
        var response = await http.PostAsync(
            $"/api/providers/{providerId}/login?method={methodIndex}",
            null, ct);

        if (!response.IsSuccessStatusCode)
            return new AuthFlowResult.Failed($"Server error: {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;

        return status switch
        {
            "ok" => new AuthFlowResult.Success(new ApiKeyEntry { Key = "" }), // entry already stored on server
            "pending" => new AuthFlowResult.PendingUserAction(
                root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "",
                root.TryGetProperty("url", out var u) ? u.GetString() : null,
                root.TryGetProperty("userCode", out var c) ? c.GetString() : null,
                // The TUI handles polling separately; WaitForCompletion is a no-op here
                _ => Task.FromResult<AuthFlowResult>(new AuthFlowResult.Cancelled())),
            "needs_input" => new AuthFlowResult.NeedsUserInput(
                root.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "",
                root.TryGetProperty("inputLabel", out var l) ? l.GetString() ?? "Input" : "Input",
                async (input, innerCt) => await CompleteLoginAsync(providerId, methodIndex, input, innerCt)),
            "cancelled" => new AuthFlowResult.Cancelled(),
            _ => new AuthFlowResult.Failed("Unexpected response from server")
        };
    }

    public async Task<AuthFlowResult> CompleteLoginAsync(string providerId, int methodIndex, string input, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync(
            $"/api/providers/{providerId}/login/complete?method={methodIndex}",
            new LoginCompleteDto(input),
            HpdosJsonOptions.Http,
            ct);

        if (!response.IsSuccessStatusCode)
            return new AuthFlowResult.Failed($"Server error: {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;

        return status switch
        {
            "ok" => new AuthFlowResult.Success(new ApiKeyEntry { Key = "" }), // stored server-side
            "cancelled" => new AuthFlowResult.Cancelled(),
            _ => new AuthFlowResult.Failed(root.TryGetProperty("error", out var e) ? e.GetString() ?? "Unknown error" : "Unknown error")
        };
    }

    public async Task<bool> LogoutAsync(string providerId)
    {
        var response = await http.DeleteAsync($"/api/providers/{providerId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> LogoutEntryAsync(string providerId, string entryId)
    {
        var response = await http.DeleteAsync($"/api/providers/{providerId}/entries/{entryId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SetActiveEntryAsync(string providerId, string entryId)
    {
        var response = await http.PutAsJsonAsync(
            $"/api/providers/{providerId}/active",
            new SetActiveRequestDto(entryId),
            HpdosJsonOptions.Http);
        return response.IsSuccessStatusCode;
    }
}

