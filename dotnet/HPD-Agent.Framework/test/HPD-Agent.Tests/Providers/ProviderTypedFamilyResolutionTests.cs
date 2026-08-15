using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;

namespace HPD.Agent.Tests.Providers;

public sealed class ProviderTypedFamilyResolutionTests
{
    [Fact]
    public void Direct_leaf_owned_family_contract_resolves_from_the_single_registry()
    {
        var registry = new ProviderRegistry();
        var provider = new AudioFamilyProvider("same");
        registry.Register(provider);

        Assert.Same(provider, registry.GetRequiredFamilyProvider<IAudioFamilyProvider>(
            "same", ProviderClientFamily.VoiceActivityDetection));
    }

    [Fact]
    public void Composite_provider_resolves_leaf_owned_contract_without_a_second_registry()
    {
        var registry = new ProviderRegistry();
        var audio = new AudioFamilyProvider("same");
        registry.Register(new ChatFamilyProvider("same"));
        registry.Register(audio);

        Assert.Same(audio, registry.GetRequiredFamilyProvider<IAudioFamilyProvider>(
            "same", ProviderClientFamily.VoiceActivityDetection));
        Assert.IsAssignableFrom<IChatClientProvider>(registry.GetProvider("same"));
        Assert.Single(registry.GetRegisteredProviders());
    }

    [Fact]
    public void Wrong_contract_or_family_fails_with_bounded_diagnostics()
    {
        var registry = new ProviderRegistry();
        registry.Register(new AudioFamilyProvider("same"));

        var wrongFamily = Assert.Throws<InvalidOperationException>(() =>
            registry.GetRequiredFamilyProvider<IAudioFamilyProvider>("same", ProviderClientFamily.Chat));
        var wrongContract = Assert.Throws<InvalidOperationException>(() =>
            registry.GetRequiredFamilyProvider<IChatClientProvider>("same", ProviderClientFamily.VoiceActivityDetection));

        Assert.Contains("Chat", wrongFamily.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IChatClientProvider), wrongContract.Message, StringComparison.Ordinal);
    }

    private interface IAudioFamilyProvider : IProvider;

    private sealed class AudioFamilyProvider(string key) : IAudioFamilyProvider
    {
        public string ProviderKey => key;
        public string DisplayName => key;
        public ProviderMetadata GetMetadata() => Metadata(key, ProviderClientFamily.VoiceActivityDetection);
        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family) =>
            ProviderValidationResult.Success();
        public IProviderErrorHandler CreateErrorHandler() => throw new NotSupportedException();
    }

    private sealed class ChatFamilyProvider(string key) : IChatClientProvider
    {
        public string ProviderKey => key;
        public string DisplayName => key;
        public ProviderMetadata GetMetadata() => Metadata(key, ProviderClientFamily.Chat);
        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family) =>
            ProviderValidationResult.Success();
        public IProviderErrorHandler CreateErrorHandler() => throw new NotSupportedException();
        public ValueTask<Microsoft.Extensions.AI.IChatClient> CreateChatClientAsync(
            ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static ProviderMetadata Metadata(string key, ProviderClientFamily family) => new()
    {
        ProviderKey = key,
        DisplayName = key,
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [family] = new() { Family = family },
        },
    };
}
