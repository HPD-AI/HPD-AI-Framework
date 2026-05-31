using System.Text.Json;
using HPD.Agent.Secrets;

internal sealed class HpdosProviderCredentialStore : ISecretResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string storePath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public HpdosProviderCredentialStore(string dataRoot)
    {
        storePath = Path.Combine(dataRoot, "provider-credentials.json");
    }

    public async ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var store = await ReadUnsafeAsync(cancellationToken);
            if (store.Credentials.TryGetValue(key, out var credential) && !string.IsNullOrWhiteSpace(credential.Value))
            {
                return new ResolvedSecret
                {
                    Value = credential.Value,
                    Source = $"local:{key}"
                };
            }

            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> HasCredentialAsync(string key, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var store = await ReadUnsafeAsync(ct);
            return store.Credentials.TryGetValue(key, out var credential)
                && !string.IsNullOrWhiteSpace(credential.Value);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveCredentialAsync(string key, string value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Credential key is required.", nameof(key));

        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Credential value is required.", nameof(value));

        await gate.WaitAsync(ct);
        try
        {
            var store = await ReadUnsafeAsync(ct);
            store.Credentials[key] = new HpdosStoredCredential(
                Value: value.Trim(),
                UpdatedAt: DateTimeOffset.UtcNow.ToString("O"));
            await WriteUnsafeAsync(store, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DeleteCredentialAsync(string key, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var store = await ReadUnsafeAsync(ct);
            var removed = store.Credentials.Remove(key);
            if (removed)
                await WriteUnsafeAsync(store, ct);
            return removed;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<HpdosProviderCredentialStoreFile> ReadUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(storePath))
            return new HpdosProviderCredentialStoreFile(1, new Dictionary<string, HpdosStoredCredential>(StringComparer.OrdinalIgnoreCase));

        await using var stream = File.OpenRead(storePath);
        var store = await JsonSerializer.DeserializeAsync<HpdosProviderCredentialStoreFile>(stream, JsonOptions, ct)
            ?? new HpdosProviderCredentialStoreFile(1, new Dictionary<string, HpdosStoredCredential>(StringComparer.OrdinalIgnoreCase));

        return new HpdosProviderCredentialStoreFile(
            Version: 1,
            Credentials: new Dictionary<string, HpdosStoredCredential>(store.Credentials, StringComparer.OrdinalIgnoreCase));
    }

    private async Task WriteUnsafeAsync(HpdosProviderCredentialStoreFile store, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        await using var stream = File.Create(storePath);
        await JsonSerializer.SerializeAsync(stream, store, JsonOptions, ct);
    }
}

internal sealed record HpdosProviderCredentialStoreFile(
    int Version,
    Dictionary<string, HpdosStoredCredential> Credentials);

internal sealed record HpdosStoredCredential(
    string Value,
    string UpdatedAt);
