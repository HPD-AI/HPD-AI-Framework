using HPD.Agent.Providers;

namespace HPD.Agent.Evaluations.Tests;

internal static class TestProviderSelections
{
    internal static ProviderReference Anonymous(string providerKey = "test") => new()
    {
        Key = providerKey,
        Backend = "platform",
        Authentication = new AnonymousProviderAuthentication()
    };

    internal static ProviderReference ApiKey(string providerKey) => new()
    {
        Key = providerKey,
        Backend = "platform",
        Authentication = new ApiKeyProviderAuthentication { SecretKey = $"{providerKey}:ApiKey" }
    };
}
