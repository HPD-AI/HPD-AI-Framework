namespace HPD.Agent.Providers;

/// <summary>Provider-package helpers for consuming the exact typed construction credential.</summary>
public static class ProviderClientConstructionUtilities
{
    /// <summary>
    /// Copies the construction-time API key into a managed string required by vendor SDK constructors.
    /// The provider must not log, cache separately, or place the returned value in configuration.
    /// </summary>
    public static string GetRequiredApiKey(ProviderCredentialBindingContext binding)
    {
        var lease = GetConstructionLease(binding);
        return lease.Credential is ProviderCredential.ApiKey apiKey
            ? apiKey.Value.Value.ToString()
            : throw new InvalidOperationException("Provider construction requires an API-key credential.");
    }

    /// <summary>Copies an API key or OAuth bearer token into the string required by a vendor SDK constructor.</summary>
    public static string GetRequiredBearerCompatibleToken(ProviderCredentialBindingContext binding)
    {
        var lease = GetConstructionLease(binding);
        return lease.Credential switch
        {
            ProviderCredential.ApiKey apiKey => apiKey.Value.Value.ToString(),
            ProviderCredential.BearerToken bearer => bearer.Value.Value.ToString(),
            _ => throw new InvalidOperationException("Provider construction requires API-key or bearer-token authentication.")
        };
    }

    /// <summary>Gets the exact SDK-native external identity with runtime type validation.</summary>
    public static TCredential GetRequiredExternalIdentity<TCredential>(ProviderCredentialBindingContext binding)
        where TCredential : class
    {
        var lease = GetConstructionLease(binding);
        if (lease.Credential is not ProviderCredential.ExternalIdentity external ||
            external.Lease.Credential is not TCredential credential)
            throw new InvalidOperationException(
                $"Provider construction requires an external identity of type '{typeof(TCredential).FullName}'.");
        return credential;
    }

    /// <summary>Verifies that the selected construction credential is anonymous.</summary>
    public static void RequireAnonymous(ProviderCredentialBindingContext binding)
    {
        if (GetConstructionLease(binding).Credential is not ProviderCredential.Anonymous)
            throw new InvalidOperationException("Provider construction requires anonymous authentication.");
    }

    /// <summary>Creates an idempotent async owner for one or more provider-owned resources.</summary>
    public static IAsyncDisposable Own(params object?[] resources) => new ResourceOwner(resources);

    private static IProviderCredentialLease GetConstructionLease(ProviderCredentialBindingContext binding) =>
        binding is ProviderCredentialBindingContext.ConstructionTime construction
            ? construction.Lease
            : throw new InvalidOperationException("This provider captures credentials during construction.");

    private sealed class ResourceOwner : IAsyncDisposable
    {
        private object?[]? _resources;

        internal ResourceOwner(object?[] resources) => _resources = resources.ToArray();

        public async ValueTask DisposeAsync()
        {
            var resources = Interlocked.Exchange(ref _resources, null);
            if (resources is null) return;
            List<Exception>? failures = null;
            var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);
            for (var index = resources.Length - 1; index >= 0; index--)
            {
                var resource = resources[index];
                if (resource is null || !disposed.Add(resource)) continue;
                try
                {
                    if (resource is IAsyncDisposable asyncDisposable)
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    else if (resource is IDisposable disposable)
                        disposable.Dispose();
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }
            if (failures is not null) throw new AggregateException(failures);
        }
    }
}
