using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Threading;
using System.Text.Json;
using GenerativeAI;
using GenerativeAI.Core;
using GenerativeAI.Microsoft;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.GoogleAI;

/// <summary>
/// Google AI (Gemini) provider implementation using the Google_GenerativeAI SDK.
/// </summary>
/// <remarks>
/// <para>
/// This provider uses Google's Generative AI SDK:
/// - Google_GenerativeAI for model access
/// - Google_GenerativeAI.Microsoft for IChatClient integration
/// </para>
/// <para>
/// Authentication: API Key (required)
/// </para>
/// </remarks>
[HpdProvider("google-ai", "Google AI (Gemini)")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "google-ai:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(GoogleAIProviderConfig), typeof(GoogleAIJsonContext))]
[HpdProviderSecretAlias("google-ai:ApiKey", "GOOGLE_API_KEY", "GEMINI_API_KEY", "GOOGLE_AI_API_KEY")]
internal class GoogleAIProvider : IProvider, IProviderClientFactory<IChatClient>, IProviderSecretAliasProvider
{
    public string ProviderKey => "google-ai";
    public string DisplayName => "Google AI (Gemini)";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("google-ai:ApiKey", new[] { "GOOGLE_API_KEY", "GEMINI_API_KEY", "GOOGLE_AI_API_KEY" }),
        };

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public ProviderClientCredentialBinding ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    public ValueTask<ProviderClientConstruction<IChatClient>> CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var config = context.EffectiveConfig;
        var googleConfig = ReadConfig(config);
        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);

        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For Google AI, the ModelName must be configured.");
        }

        var chatClient = new GenerativeAIChatClient(
            new GoogleAIPlatformAdapter(
                apiKey,
                apiVersion: googleConfig.ApiVersion ?? ApiVersions.v1Beta,
                validateAccessToken: googleConfig.ValidateAccessToken),
            modelName,
            autoCallFunction: true);

        IChatClient finalClient = chatClient;
        return ValueTask.FromResult(new ProviderClientConstruction<IChatClient>
        {
            Client = finalClient,
            Owner = ProviderClientConstructionUtilities.Own(finalClient, chatClient)
        });
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new GoogleAIErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://ai.google.dev/docs"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["SupportsFunctionCalling"] = true,
                        ["SupportsVision"] = true
                    }
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        var errors = new List<string>();
        var googleConfig = ReadConfig(config);

        // Credential availability is validated by the authentication coordinator during acquisition.
        // This method only validates config structure, not secret resolution.

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name is required");

        if (config.Family != ProviderClientFamily.Chat)
            errors.Add("Google AI supports only chat.");

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    private static GoogleAIProviderConfig ReadConfig(EffectiveProviderClientConfig config) =>
        config.ProviderConfiguration.CanonicalPayload.IsEmpty
            ? new GoogleAIProviderConfig()
            : JsonSerializer.Deserialize(
                config.ProviderConfiguration.CanonicalPayload.AsSpan(),
                GoogleAIJsonContext.Default.GoogleAIProviderConfig) ?? new GoogleAIProviderConfig();
}
