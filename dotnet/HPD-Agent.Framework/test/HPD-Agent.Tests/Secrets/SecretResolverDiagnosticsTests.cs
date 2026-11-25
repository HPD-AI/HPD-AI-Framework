using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Tests.Secrets;

public sealed class SecretResolverDiagnosticsTests
{
    [Fact]
    public void EnvironmentGuidance_UsesAliasesForExactReferenceOnly()
    {
        var environment = new EnvironmentSecretResolver(new Aliases());
        var resolver = new ChainedSecretResolver(new CachingSecretResolver(environment));

        Assert.Equal("Set one of these environment variables: TEST_PROVIDER_API_KEY.",
            resolver.GetSetupGuidance("test:ApiKey"));
        Assert.Null(resolver.GetSetupGuidance("custom:ApiKey"));
    }

    private sealed class Aliases : IProviderSecretAliasRegistry
    {
        public IReadOnlyList<string>? GetEnvironmentVariables(string secretKey) =>
            secretKey == "test:ApiKey" ? ["TEST_PROVIDER_API_KEY"] : null;
    }
}
