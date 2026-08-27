using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;
using HPD.Agent;
namespace HPD.Agent.Providers.OpenRouter;

[HpdProvider("openrouter", "OpenRouter", DocumentationUrl = "https://openrouter.ai/docs")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "openrouter:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(OpenRouterProviderConfig), typeof(OpenRouterJsonContext))]
[HpdProviderSecretAlias("openrouter:ApiKey", "OPENROUTER_API_KEY")]
internal class OpenRouterProvider : IProvider, IProviderClientFactory<IChatClient>, IProviderSecretAliasProvider
{
    public string ProviderKey => "openrouter";
    public string DisplayName => "OpenRouter";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("openrouter:ApiKey", new[] { "OPENROUTER_API_KEY" }),
        };

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
        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);

        var attributionInfo = ExtractAttributionInfo(config);

        var httpClient = context.Services.HttpClientFactory.CreateClient("hpd-provider-openrouter");
        httpClient.BaseAddress = config.Endpoint ?? new Uri("https://openrouter.ai/api/v1/");

        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        httpClient.DefaultRequestHeaders.Add("HTTP-Referer", attributionInfo.Referer);
        httpClient.DefaultRequestHeaders.Add("X-Title", attributionInfo.Title);

        IChatClient client = new OpenRouterChatClient(httpClient, config.ModelName);
        return ValueTask.FromResult(new ProviderClientConstruction<IChatClient>
        {
            Client = client,
            Owner = ProviderClientConstructionUtilities.Own(client, httpClient)
        });
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new OpenRouterErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://openrouter.ai/docs"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["SupportsFunctionCalling"] = true,
                        ["SupportsVision"] = true,
                        ["SupportsAttribution"] = true,
                        ["SupportsModelRouting"] = true,
                        ["SupportsFallbackModels"] = true,
                        ["SupportsProviderRouting"] = true,
                        ["SupportsPriceFiltering"] = true,
                        ["SupportsZeroDataRetention"] = true,
                        ["AttributionRequirements"] = "Set OpenRouterProviderConfig.HttpReferer and OpenRouterProviderConfig.AppName for app rankings"
                    }
                }
            }
        };
    }

    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        // Credential availability is validated by the authentication coordinator during acquisition.
        // This method only validates config structure, not secret resolution
        if (string.IsNullOrEmpty(config.ModelName))
            return ProviderValidationResult.Failure("Model name is required");

        return ProviderValidationResult.Success();
    }

    /// <summary>
    /// Extracts and validates attribution information for OpenRouter app ranking and analytics.
    /// </summary>
    /// <param name="config">The provider configuration.</param>
    /// <returns>Attribution information with referer and title.</returns>
    private static AttributionInfo ExtractAttributionInfo(EffectiveProviderClientConfig config)
    {
        var attribution = new AttributionInfo();

        var providerConfig = config.ProviderConfiguration.CanonicalPayload.IsEmpty
            ? null
            : JsonSerializer.Deserialize(
                config.ProviderConfiguration.CanonicalPayload.AsSpan(),
                OpenRouterJsonContext.Default.OpenRouterProviderConfig);
        attribution.Referer = providerConfig?.HttpReferer ?? string.Empty;
        attribution.Title = providerConfig?.AppName ?? string.Empty;

        // Apply defaults and validation
        attribution.ApplyDefaults();
        
        return attribution;
    }

    /// <summary>
    /// Attribution information for OpenRouter app analytics and rankings.
    /// </summary>
    private class AttributionInfo
    {
        public string Referer { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Applies default values and validates attribution information according to OpenRouter best practices.
        /// </summary>
        public void ApplyDefaults()
        {
            // Default referer if not provided
            if (string.IsNullOrEmpty(Referer))
            {
                // Try to detect if we're in development/localhost
                if (IsLocalDevelopment())
                {
                    Referer = "http://localhost"; // OpenRouter requires a referer for tracking
                }
                else
                {
                    // Use HPD-Agent GitHub as fallback for library attribution
                    Referer = "https://github.com/hpd-agent/hpd-agent";
                }
            }

            // Default title if not provided
            if (string.IsNullOrEmpty(Title))
            {
                // For localhost, we need a title to be tracked
                if (Referer.Contains("localhost") || IsLocalDevelopment())
                {
                    Title = GetDefaultDevelopmentTitle();
                }
                else
                {
                    Title = "HPD-Agent Application";
                }
            }

            // Validate and clean up
            Referer = CleanReferer(Referer);
            Title = CleanTitle(Title);
        }

        private static bool IsLocalDevelopment()
        {
            // Simple heuristic to detect development environment
            try
            {
                var currentDirectory = Directory.GetCurrentDirectory();
                return currentDirectory.Contains("bin") || 
                       currentDirectory.Contains("Debug") || 
                       currentDirectory.Contains("obj") ||
                       System.Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == null;
            }
            catch
            {
                return false;
            }
        }

        private static string GetDefaultDevelopmentTitle()
        {
            try
            {
                // Try to get a meaningful title from the entry assembly
                var entryAssembly = Assembly.GetEntryAssembly();
                if (entryAssembly != null)
                {
                    var assemblyName = entryAssembly.GetName().Name;
                    if (!string.IsNullOrEmpty(assemblyName) && assemblyName != "HPD-Agent")
                    {
                        return $"{assemblyName} (Dev)";
                    }
                }
            }
            catch { }

            return "HPD-Agent Development";
        }

        private static string CleanReferer(string referer)
        {
            if (string.IsNullOrEmpty(referer))
                return "https://github.com/hpd-agent";

            // Ensure it's a valid URL format
            if (!referer.StartsWith("http://") && !referer.StartsWith("https://"))
            {
                // Assume https for production domains
                if (referer.Contains("localhost") || referer.Contains("127.0.0.1"))
                {
                    referer = "http://" + referer;
                }
                else
                {
                    referer = "https://" + referer;
                }
            }

            return referer;
        }

        private static string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return "HPD-Agent";

            // Ensure title is reasonable length and not generic
            title = title.Trim();
            if (title.Length > 50)
            {
                title = title.Substring(0, 50).Trim() + "...";
            }

            // Avoid completely generic titles
            var genericTitles = new[] { "AI App", "Chatbot", "App", "Application", "Test" };
            if (genericTitles.Contains(title))
            {
                title = $"HPD-Agent {title}";
            }

            return title;
        }
    }
}
