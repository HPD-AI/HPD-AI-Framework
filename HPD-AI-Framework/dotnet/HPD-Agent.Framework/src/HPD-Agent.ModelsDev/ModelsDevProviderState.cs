using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.ModelsDev;

public interface IModelsDevProviderState
{
    ValueTask<ModelsDevProviderStatus> GetStatusAsync(
        string hpdProviderKey,
        CancellationToken cancellationToken = default);
}

public readonly record struct ModelsDevProviderStatus(
    bool IsRegistered,
    bool IsAuthenticated,
    bool IsExpired = false);

public sealed class StaticModelsDevProviderState : IModelsDevProviderState
{
    private readonly Dictionary<string, ModelsDevProviderStatus> _statuses;

    public StaticModelsDevProviderState(IReadOnlyDictionary<string, ModelsDevProviderStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        _statuses = new Dictionary<string, ModelsDevProviderStatus>(statuses, StringComparer.OrdinalIgnoreCase);
    }

    public ValueTask<ModelsDevProviderStatus> GetStatusAsync(
        string hpdProviderKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_statuses.TryGetValue(hpdProviderKey, out var status)
            ? status
            : default);
    }
}

public sealed class HpdModelsDevProviderState : IModelsDevProviderState
{
    private readonly IProviderRegistry? _providerRegistry;
    private readonly ISecretResolver? _secretResolver;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _requiredSecretKeys;

    public HpdModelsDevProviderState(
        IProviderRegistry? providerRegistry = null,
        ISecretResolver? secretResolver = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? requiredSecretKeys = null)
    {
        _providerRegistry = providerRegistry;
        _secretResolver = secretResolver;
        _requiredSecretKeys = requiredSecretKeys ?? DefaultRequiredSecretKeys.Value;
    }

    public async ValueTask<ModelsDevProviderStatus> GetStatusAsync(
        string hpdProviderKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hpdProviderKey);

        var isRegistered = _providerRegistry?.IsRegistered(hpdProviderKey) ?? false;
        var isAuthenticated = await IsAuthenticatedAsync(hpdProviderKey, cancellationToken);

        return new ModelsDevProviderStatus(isRegistered, isAuthenticated);
    }

    private async ValueTask<bool> IsAuthenticatedAsync(
        string hpdProviderKey,
        CancellationToken cancellationToken)
    {
        if (_secretResolver is null)
        {
            return false;
        }

        if (_requiredSecretKeys.TryGetValue(hpdProviderKey, out var configuredKeys))
        {
            if (configuredKeys.Count == 0)
            {
                return true;
            }

            foreach (var key in configuredKeys)
            {
                var resolved = await _secretResolver.ResolveAsync(key, cancellationToken);
                if (resolved is null || string.IsNullOrWhiteSpace(resolved.Value.Value))
                {
                    return false;
                }
            }

            return true;
        }

        var secret = await _secretResolver.ResolveAsync($"{hpdProviderKey}:ApiKey", cancellationToken);
        return secret is not null && !string.IsNullOrWhiteSpace(secret.Value.Value);
    }

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<string>>> DefaultRequiredSecretKeys = new(static () =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["openai"] = ["openai:ApiKey"],
            ["azure-openai"] = ["azure-openai:ApiKey", "azure-openai:Endpoint"],
            ["anthropic"] = ["anthropic:ApiKey"],
            ["azure-ai"] = ["azure-ai:ApiKey", "azure-ai:Endpoint"],
            ["google-ai"] = ["google-ai:ApiKey"],
            ["mistral"] = ["mistral:ApiKey"],
            ["openrouter"] = ["openrouter:ApiKey"],
            ["huggingface"] = ["huggingface:ApiKey"],
            ["bedrock"] = ["bedrock:AccessKeyId", "bedrock:SecretAccessKey"],
            ["ollama"] = []
        });
}
