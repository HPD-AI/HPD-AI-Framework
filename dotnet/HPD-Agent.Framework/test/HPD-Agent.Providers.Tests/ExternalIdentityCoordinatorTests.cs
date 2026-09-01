using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.Tests;

public sealed class ExternalIdentityCoordinatorTests
{
    [Fact]
    public async Task OwnedExternalIdentity_HasStableGenerationAndDisposesEachAcquiredInstanceExactlyOnce()
    {
        var credentials = new List<TestCredential>();
        var registration = new ProviderExternalIdentityRegistration<TestCredential>(
            "azure-work",
            () =>
            {
                var credential = new TestCredential();
                credentials.Add(credential);
                return credential;
            });
        var coordinator = new ProviderAuthenticationCoordinator(
            new TestDictionarySecretResolver(),
            externalIdentities: new ProviderExternalIdentityRegistry([registration]));
        var plan = await coordinator.PrepareAsync(Request());

        var first = await coordinator.AcquireAsync(plan);
        var second = await coordinator.AcquireAsync(plan);

        Assert.Equal(first.Generation, second.Generation);
        Assert.NotSame(
            Assert.IsType<ProviderCredential.ExternalIdentity>(first.Credential).Lease.Credential,
            Assert.IsType<ProviderCredential.ExternalIdentity>(second.Credential).Lease.Credential);
        await first.DisposeAsync();
        await first.DisposeAsync();
        await second.DisposeAsync();

        Assert.Equal(2, credentials.Count);
        Assert.All(credentials, credential => Assert.Equal(1, credential.DisposeCount));
    }

    [Fact]
    public async Task BorrowedExternalIdentity_IsNeverDisposedByCredentialLease()
    {
        var credential = new TestCredential();
        var registration = new ProviderExternalIdentityRegistration<TestCredential>(
            "azure-borrowed",
            _ => ValueTask.FromResult<(TestCredential Credential, IAsyncDisposable? Owner)>((credential, null)));
        var coordinator = new ProviderAuthenticationCoordinator(
            new TestDictionarySecretResolver(),
            externalIdentities: new ProviderExternalIdentityRegistry([registration]));
        var plan = await coordinator.PrepareAsync(Request("azure-borrowed"));

        await using (var lease = await coordinator.AcquireAsync(plan))
            Assert.Same(credential, Assert.IsType<ProviderCredential.ExternalIdentity>(lease.Credential).Lease.Credential);

        Assert.Equal(0, credential.DisposeCount);
        await credential.DisposeAsync();
    }

    private static ProviderCredentialRequest Request(string name = "azure-work") => new()
    {
        ProviderKey = "azure-ai",
        BackendKey = "azure",
        Family = ProviderClientFamily.Chat,
        Authentication = new ExternalIdentityProviderAuthentication { CredentialName = name },
        AuthorizationScope = new ProviderAuthorizationScope { TrustDomainId = "test-host" },
        Audience = new ProviderCredentialAudience { Audience = "https://cognitiveservices.azure.com/.default" }
    };

    private sealed class TestCredential : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
