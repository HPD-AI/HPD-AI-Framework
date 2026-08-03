using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Tests.Core;

public sealed class ProviderCredentialLeaseTests
{
    [Fact]
    public async Task ExplicitLease_IsUniqueAndBecomesUnreadableAfterDisposal()
    {
        var first = ProviderCredentialLease.CreateExplicit("secret");
        var second = ProviderCredentialLease.CreateExplicit("secret");

        Assert.NotEqual(first.Identity, second.Identity);
        Assert.Equal("secret", first.Secret.ToString());
        await first.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => first.Secret);
        await second.DisposeAsync();
    }

    [Fact]
    public async Task SecretResolverAdapter_ReturnsIdentityAndSecretAtomically()
    {
        var secrets = new ExplicitSecretResolver();
        secrets.Set("openai:ApiKey", "resolved");
        var resolver = new SecretResolverProviderCredentialResolver(secrets);

        await using var lease = await resolver.AcquireAsync(new ProviderCredentialRequest
        {
            ProviderKey = "openai",
            Family = ProviderClientFamily.Chat,
            Identity = "registration:work",
            SecretKey = "openai:ApiKey",
            AuthorizationScope = new ProviderAuthorizationScope { TrustDomainId = "test-host" }
        });

        Assert.Equal("registration:work", lease.Identity);
        Assert.Equal("resolved", lease.Secret.ToString());
        Assert.Equal(0, lease.Generation);
    }
}
