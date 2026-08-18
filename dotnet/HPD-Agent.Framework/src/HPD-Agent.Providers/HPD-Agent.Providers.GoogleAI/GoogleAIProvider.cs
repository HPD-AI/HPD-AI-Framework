using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Threading;
using GenerativeAI;
using GenerativeAI.Core;
using GenerativeAI.Microsoft;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

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
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(GoogleAIProviderConfig), typeof(GoogleAIJsonContext))]
[HpdProviderSecretAlias("google-ai:ApiKey", "GOOGLE_API_KEY", "GEMINI_API_KEY", "GOOGLE_AI_API_KEY")]
internal class GoogleAIProvider : IChatClientProvider, IProviderSecretAliasProvider
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
    public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Get secret resolver from services
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets == null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        var googleConfig = config.ProviderConfig as GoogleAIProviderConfig ?? new GoogleAIProviderConfig();
        var apiKey = ResolveApiKey(secrets, config, googleConfig);

        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For Google AI, the ModelName must be configured.");
        }

        var chatClient = new GenerativeAIChatClient(
            CreatePlatformAdapter(apiKey, googleConfig),
            modelName,
            autoCallFunction: true);

        IChatClient finalClient = chatClient;

        return finalClient;
    }

    private static string? ResolveApiKey(
        ISecretResolver secrets,
        ProviderClientConfig config,
        GoogleAIProviderConfig googleConfig)
    {
        if (RequiresApiKey(googleConfig))
        {
            var apiKeyTask = secrets.RequireAsync(
                "google-ai:ApiKey",
                "Google AI",
                config.ApiKey,
                CancellationToken.None);
            return apiKeyTask.GetAwaiter().GetResult();
        }

        var optionalApiKeyTask = secrets.ResolveOrDefaultAsync(
            "google-ai:ApiKey",
            config.ApiKey,
            CancellationToken.None);
        return optionalApiKeyTask.GetAwaiter().GetResult();
    }

    private static bool RequiresApiKey(GoogleAIProviderConfig googleConfig)
        => googleConfig.Platform == GoogleAIPlatform.GeminiDeveloperApi ||
           googleConfig.ExpressMode;

    private static IPlatformAdapter CreatePlatformAdapter(
        string? apiKey,
        GoogleAIProviderConfig googleConfig)
    {
        return googleConfig.Platform switch
        {
            GoogleAIPlatform.GeminiDeveloperApi => new GoogleAIPlatformAdapter(
                apiKey,
                apiVersion: googleConfig.ApiVersion ?? ApiVersions.v1Beta,
                validateAccessToken: googleConfig.ValidateAccessToken),

            GoogleAIPlatform.VertexAI => new VertextPlatformAdapter(
                projectId: googleConfig.ProjectId,
                region: googleConfig.Region,
                expressMode: googleConfig.ExpressMode,
                apiKey: apiKey,
                apiVersion: googleConfig.ApiVersion ?? ApiVersions.v1Beta1,
                credentialsFile: googleConfig.CredentialsFile,
                validateAccessToken: googleConfig.ValidateAccessToken),

            _ => throw new InvalidOperationException(
                $"Unsupported Google AI platform '{googleConfig.Platform}'.")
        };
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
    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        var errors = new List<string>();
        var googleConfig = config.ProviderConfig as GoogleAIProviderConfig ?? new GoogleAIProviderConfig();

        // API key validation is deferred to CreateChatClient where ISecretResolver is available.
        // This method only validates config structure, not secret resolution.

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name is required");

        if (!Enum.IsDefined(googleConfig.Platform))
            errors.Add($"Unsupported Google AI platform '{googleConfig.Platform}'.");

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }
}
