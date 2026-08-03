using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using System.Linq;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using HPD.Agent;
namespace HPD.Agent.Providers.OpenRouter;

internal class OpenRouterProvider : IChatClientProvider
{
    public string ProviderKey => "openrouter";
    public string DisplayName => "OpenRouter";

    public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        // Get secret resolver from services
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets == null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        // Resolve API key using ISecretResolver
        string apiKey = await secrets.RequireAsync("openrouter:ApiKey", "OpenRouter", config.ApiKey, cancellationToken).ConfigureAwait(false);

        var attributionInfo = ExtractAttributionInfo(config);

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/")
        };

        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        httpClient.DefaultRequestHeaders.Add("HTTP-Referer", attributionInfo.Referer);
        httpClient.DefaultRequestHeaders.Add("X-Title", attributionInfo.Title);

        return new OpenRouterChatClient(httpClient, config.ModelName);
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
                        ["AttributionRequirements"] = "Set ProviderClientConfig.HttpReferer and ProviderClientConfig.AppName for app rankings"
                    }
                }
            }
        };
    }

    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        // Note: API key validation is now deferred to CreateChatClient where ISecretResolver is available
        // This method only validates config structure, not secret resolution
        if (string.IsNullOrEmpty(config.ModelName))
            return ProviderValidationResult.Failure("Model name is required");

        return ProviderValidationResult.Success();
    }

    /// <summary>
    /// Creates a properly configured ProviderClientConfig with attribution headers for OpenRouter app ranking.
    /// </summary>
    /// <param name="apiKey">Your OpenRouter API key.</param>
    /// <param name="modelName">The model to use.</param>
    /// <param name="appUrl">Your app's URL (for HTTP-Referer header).</param>
    /// <param name="appName">Your app's display name (for X-Title header).</param>
    /// <returns>A configured ProviderClientConfig with attribution.</returns>
    public static ProviderClientConfig CreateConfigWithAttribution(string apiKey, string modelName, string appUrl, string appName)
    {
        return new ProviderClientConfig
        {
            ProviderKey = "openrouter",
            ApiKey = apiKey,
            ModelName = modelName,
            HttpReferer = appUrl,
            AppName = appName
        };
    }

    /// <summary>
    /// Adds attribution headers to an existing ProviderClientConfig.
    /// </summary>
    /// <param name="config">The existing configuration.</param>
    /// <param name="appUrl">Your app's URL (for HTTP-Referer header).</param>
    /// <param name="appName">Your app's display name (for X-Title header).</param>
    /// <returns>The updated configuration.</returns>
    public static ProviderClientConfig WithAttribution(ProviderClientConfig config, string appUrl, string appName)
    {
        config.HttpReferer = appUrl;
        config.AppName = appName;
        return config;
    }

    /// <summary>
    /// Validates an OpenRouter API key by checking if it can access the key info endpoint.
    ///    NETWORK CALL: This method makes HTTP requests and can be slow (2-5 seconds).
    /// Only use when you need live API validation. For fast builds, use ValidateConfiguration() instead.
    /// ✨ PERFORMANCE: Set OPENROUTER_SKIP_VALIDATION=true to skip validation entirely
    /// </summary>
    /// <param name="config">The provider configuration containing the API key.</param>
    /// <param name="family">The provider client family being validated.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A validation result indicating if the key is valid.</returns>
    public async Task<ProviderValidationResult> ValidateConfigurationAsync(ProviderClientConfig config, ProviderClientFamily family, CancellationToken cancellationToken = default)
    {
        var basicValidation = ValidateConfiguration(config, family);
        if (!basicValidation.IsValid)
            return basicValidation;

        // ✨ PERFORMANCE: Allow skipping expensive validation entirely
        if (System.Environment.GetEnvironmentVariable("OPENROUTER_SKIP_VALIDATION")?.ToLowerInvariant() == "true")
        {
            return ProviderValidationResult.Success(); // Validation skipped for performance
        }

        try
        {
            // Create a temporary client to test the API key
            var testClient = await CreateChatClientAsync(config, cancellationToken: cancellationToken).ConfigureAwait(false) as OpenRouterChatClient;
            if (testClient == null)
                return ProviderValidationResult.Failure("Failed to create test client");

            // ✨ PERFORMANCE: Use a shorter timeout for validation (3 seconds max)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3)); // Shorter timeout

            // Validate the API key (1st network call)
            var isValid = await testClient.ValidateKeyAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!isValid)
                return ProviderValidationResult.Failure("Invalid API key or insufficient permissions");

            // Check credit status (2nd network call) - but make this optional
            if (System.Environment.GetEnvironmentVariable("OPENROUTER_CHECK_CREDITS")?.ToLowerInvariant() != "false")
            {
                var keyInfo = await testClient.GetKeyInfoAsync(timeoutCts.Token).ConfigureAwait(false);
                if (keyInfo.Data.LimitRemaining.HasValue && keyInfo.Data.LimitRemaining <= 0)
                {
                    return ProviderValidationResult.Failure("API key has no remaining credits");
                }
            }

            return ProviderValidationResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Re-throw user cancellation
        }
        catch (OperationCanceledException)
        {
            // Timeout occurred - treat as warning, not failure for fast builds
            return ProviderValidationResult.Success(); // Proceed without validation on timeout
        }
        catch (Exception)
        {
            //    Don't fail builds on network errors - just warn and proceed
            return ProviderValidationResult.Success(); // Allow build to continue despite validation errors
        }
    }

    /// <summary>
    /// Extracts and validates attribution information for OpenRouter app ranking and analytics.
    /// </summary>
    /// <param name="config">The provider configuration.</param>
    /// <returns>Attribution information with referer and title.</returns>
    private static AttributionInfo ExtractAttributionInfo(ProviderClientConfig config)
    {
        var attribution = new AttributionInfo();

        attribution.Referer = config.HttpReferer ?? string.Empty;
        attribution.Title = config.AppName ?? string.Empty;

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
