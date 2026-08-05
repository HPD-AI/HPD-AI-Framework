using HPD.Agent.Providers;

namespace HPD.Agent.Tests.Core;

public sealed class ProviderAuthenticationRegistryTests
{
    [Fact]
    public async Task FindAsync_ReturnsOnlyProviderAndFamilyCompatibleRegistration()
    {
        var registry = new InMemoryProviderAuthenticationRegistry();
        registry.Register(new ProviderAuthenticationRegistration
        {
            Key = "openai-work",
            ProviderKey = "openai",
            SecretKey = "openai:work:ApiKey",
            Families = new HashSet<ProviderClientFamily> { ProviderClientFamily.Chat }
        });

        var chat = await registry.FindAsync("openai-work", new ProviderAuthenticationContext
        {
            ProviderKey = "openai",
            Family = ProviderClientFamily.Chat
        });
        var realtime = await registry.FindAsync("openai-work", new ProviderAuthenticationContext
        {
            ProviderKey = "openai",
            Family = ProviderClientFamily.Realtime
        });
        var anthropic = await registry.FindAsync("openai-work", new ProviderAuthenticationContext
        {
            ProviderKey = "anthropic",
            Family = ProviderClientFamily.Chat
        });

        Assert.NotNull(chat);
        Assert.Null(realtime);
        Assert.Null(anthropic);
    }

    [Fact]
    public async Task ListCompatibleAsync_DoesNotReturnOtherProviderRegistrations()
    {
        var registry = new InMemoryProviderAuthenticationRegistry();
        registry.Register(new ProviderAuthenticationRegistration
        {
            Key = "openai-work",
            ProviderKey = "openai",
            SecretKey = "openai:work:ApiKey"
        });
        registry.Register(new ProviderAuthenticationRegistration
        {
            Key = "anthropic-work",
            ProviderKey = "anthropic",
            SecretKey = "anthropic:work:ApiKey"
        });

        var registrations = new List<ProviderAuthenticationRegistration>();
        await foreach (var registration in registry.ListCompatibleAsync(new ProviderAuthenticationContext
        {
            ProviderKey = "openai",
            Family = ProviderClientFamily.Chat
        }))
        {
            registrations.Add(registration);
        }

        Assert.Single(registrations);
        Assert.Equal("openai-work", registrations[0].Key);
    }

    [Fact]
    public void Register_IsIdempotentOnlyForEquivalentRegistration()
    {
        var registry = new InMemoryProviderAuthenticationRegistry();
        var registration = new ProviderAuthenticationRegistration
        {
            Key = "openai-work",
            ProviderKey = "openai",
            SecretKey = "openai:work:ApiKey",
            Families = new HashSet<ProviderClientFamily> { ProviderClientFamily.Chat }
        };

        registry.Register(registration);
        registry.Register(registration with { Families = new HashSet<ProviderClientFamily> { ProviderClientFamily.Chat } });
        var exception = Assert.Throws<ProviderAuthenticationRegistrationException>(() =>
            registry.Register(registration with { SecretKey = "openai:other:ApiKey" }));

        Assert.Equal("DuplicateCredentialRegistration", exception.Code);
    }

    [Fact]
    public async Task FindAsync_RequiresExactConfiguredTrustScope()
    {
        var registry = new InMemoryProviderAuthenticationRegistry();
        registry.Register(new ProviderAuthenticationRegistration
        {
            Key = "tenant-key",
            ProviderKey = "openai",
            SecretKey = "openai:tenant:ApiKey",
            RequiredScope = new ProviderAuthorizationScope
            {
                TrustDomainId = "host",
                TenantId = "tenant-a",
                PrincipalId = "user-a"
            }
        });

        var denied = await registry.FindAsync("tenant-key", new ProviderAuthenticationContext
        {
            ProviderKey = "openai",
            Family = ProviderClientFamily.Chat,
            AuthorizationScope = new ProviderAuthorizationScope
            {
                TrustDomainId = "host",
                TenantId = "tenant-b",
                PrincipalId = "user-a"
            }
        });

        Assert.Null(denied);
    }
}
