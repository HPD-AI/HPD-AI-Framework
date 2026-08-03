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
}
