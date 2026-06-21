// HPD-Agent/Providers/ProviderRegistryExtensions.cs
using System;
using System.Linq;

namespace HPD.Agent.Providers;

/// <summary>
/// Helpers for resolving provider family contracts with consistent diagnostics.
/// </summary>
public static class ProviderRegistryExtensions
{
    public static TProvider GetRequiredProvider<TProvider>(
        this IProviderRegistry registry,
        string providerKey)
        where TProvider : class, IProvider
    {
        ArgumentNullException.ThrowIfNull(registry);

        var typedProvider = registry.GetProvider<TProvider>(providerKey);
        if (typedProvider != null)
            return typedProvider;

        var provider = registry.GetProvider(providerKey);
        if (provider == null)
        {
            throw new InvalidOperationException(
                $"Provider '{providerKey}' is not registered. Available providers: {string.Join(", ", registry.GetRegisteredProviders())}");
        }

        var family = GetRequiredFamilyName<TProvider>();
        var metadata = provider.GetMetadata();
        var supportedFamilies = metadata.Families.Count == 0
            ? "none"
            : string.Join(", ", metadata.Families.Keys.OrderBy(static family => family.ToString()));

        throw new InvalidOperationException(
            $"Provider '{providerKey}' is registered, but it does not support client family '{family}'. Supported families: {supportedFamilies}.");
    }

    private static string GetRequiredFamilyName<TProvider>()
        where TProvider : class, IProvider
    {
        var providerType = typeof(TProvider);

        if (providerType == typeof(IChatClientProvider))
            return ProviderClientFamily.Chat.ToString();
        if (providerType == typeof(ITextToSpeechClientProvider))
            return ProviderClientFamily.TextToSpeech.ToString();
        if (providerType == typeof(ISpeechToTextClientProvider))
            return ProviderClientFamily.SpeechToText.ToString();
        if (providerType == typeof(IRealtimeClientProvider))
            return ProviderClientFamily.Realtime.ToString();
        if (providerType == typeof(IImageGeneratorProvider))
            return ProviderClientFamily.ImageGeneration.ToString();
        if (providerType == typeof(IEmbeddingGeneratorProvider))
            return ProviderClientFamily.Embeddings.ToString();
        if (providerType == typeof(IHostedFileClientProvider))
            return ProviderClientFamily.HostedFiles.ToString();
        if (providerType == typeof(IVoiceActivityDetectorProvider))
            return ProviderClientFamily.VoiceActivityDetection.ToString();
        if (providerType == typeof(IEndOfTurnDetectorProvider))
            return ProviderClientFamily.EndOfTurnDetection.ToString();

        return providerType.Name;
    }
}
