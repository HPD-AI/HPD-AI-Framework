using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001

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

    [Fact]
    public void Resolved_handle_captures_config_and_generated_lifetime_at_the_resolution_cut()
    {
        var registry = new ProviderRegistry();
        var provider = new AudioFamilyProvider("same", ProviderFamilyLifetime.StatefulPerAudioSession);
        registry.Register(provider);
        var configuration = new ProviderClientConfig
        {
            ProviderKey = "same",
            ModelName = "captured",
            CustomHeaders = new() { ["x-safe"] = "one" },
        };

        var resolved = registry.ResolveRequiredFamily<IAudioFamilyProvider>(configuration,
            ProviderClientFamily.VoiceActivityDetection, ProviderFamilyLifetime.StatefulPerRun);
        configuration.ModelName = "mutated";
        configuration.CustomHeaders["x-safe"] = "two";
        var firstCopy = resolved.Configuration;
        firstCopy.ModelName = "copy-mutated";

        Assert.Same(provider, resolved.Provider);
        Assert.Equal(ProviderFamilyLifetime.StatefulPerAudioSession, resolved.Lifetime);
        Assert.Equal("captured", resolved.Configuration.ModelName);
        Assert.Equal("one", resolved.Configuration.CustomHeaders!["x-safe"]);
    }

    [Fact]
    public void Leaf_family_composition_preserves_existing_cross_family_precedence()
    {
        var registry = new ProviderRegistry();
        var chat = new ChatFamilyProvider("same");
        var audio = new AudioFamilyProvider("same");
        var speech = new ExistingAudioFamiliesProvider("same");
        registry.Register(chat);
        registry.Register(speech);
        registry.Register(audio);

        Assert.Same(audio, registry.GetRequiredFamilyProvider<IAudioFamilyProvider>(
            "same", ProviderClientFamily.VoiceActivityDetection));
        Assert.Same(chat, registry.GetRequiredFamilyProvider<IChatClientProvider>(
            "same", ProviderClientFamily.Chat));
        Assert.Same(speech, registry.GetRequiredFamilyProvider<ISpeechToTextClientProvider>(
            "same", ProviderClientFamily.SpeechToText));
        Assert.Same(speech, registry.GetRequiredFamilyProvider<ITextToSpeechClientProvider>(
            "same", ProviderClientFamily.TextToSpeech));
        Assert.Same(speech, registry.GetRequiredFamilyProvider<IRealtimeClientProvider>(
            "same", ProviderClientFamily.Realtime));
        Assert.Single(registry.GetRegisteredProviders());
    }

    private interface IAudioFamilyProvider : IProvider;

    private sealed class AudioFamilyProvider(
        string key,
        ProviderFamilyLifetime lifetime = ProviderFamilyLifetime.ReusableClient) : IAudioFamilyProvider
    {
        public string ProviderKey => key;
        public string DisplayName => key;
        public ProviderMetadata GetMetadata() => Metadata(key, ProviderClientFamily.VoiceActivityDetection, lifetime);
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

    private sealed class ExistingAudioFamiliesProvider(string key) :
        ISpeechToTextClientProvider, ITextToSpeechClientProvider, IRealtimeClientProvider
    {
        public string ProviderKey => key;
        public string DisplayName => key;
        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = key,
            DisplayName = key,
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.SpeechToText] = new() { Family = ProviderClientFamily.SpeechToText },
                [ProviderClientFamily.TextToSpeech] = new() { Family = ProviderClientFamily.TextToSpeech },
                [ProviderClientFamily.Realtime] = new() { Family = ProviderClientFamily.Realtime },
            },
        };
        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family) =>
            ProviderValidationResult.Success();
        public IProviderErrorHandler CreateErrorHandler() => throw new NotSupportedException();
        public ISpeechToTextClient CreateSpeechToTextClient(ProviderClientConfig config, IServiceProvider? services = null) =>
            throw new NotSupportedException();
        public ITextToSpeechClient CreateTextToSpeechClient(ProviderClientConfig config, IServiceProvider? services = null) =>
            throw new NotSupportedException();
        public IRealtimeClient CreateRealtimeClient(ProviderClientConfig config, IServiceProvider? services = null) =>
            throw new NotSupportedException();
    }

    private static ProviderMetadata Metadata(string key, ProviderClientFamily family,
        ProviderFamilyLifetime lifetime = ProviderFamilyLifetime.ReusableClient) => new()
    {
        ProviderKey = key,
        DisplayName = key,
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [family] = new() { Family = family, Lifetime = lifetime },
        },
    };
}
#pragma warning restore MEAI001
