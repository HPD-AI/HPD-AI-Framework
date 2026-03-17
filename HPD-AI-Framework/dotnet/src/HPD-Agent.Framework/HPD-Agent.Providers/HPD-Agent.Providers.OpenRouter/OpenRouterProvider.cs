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

internal class OpenRouterProvider : IProviderExtendedFeatures
{
    public string ProviderKey => "openrouter";
    public string DisplayName => "OpenRouter";
    public bool SupportsCreditManagement => true;
    public bool SupportsAttribution => true;
    public bool SupportsModelRouting => true;

    public IChatClient CreateChatClient(ProviderConfig config, IServiceProvider? services = null)
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
        var apiKeyTask = secrets.RequireAsync("openrouter:ApiKey", "OpenRouter", config.ApiKey, CancellationToken.None);
        string apiKey = apiKeyTask.GetAwaiter().GetResult();

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
            SupportsStreaming = true,
            SupportsFunctionCalling = true,
            SupportsVision = true, // OpenRouter supports vision, audio, video, and PDF processing
            DocumentationUrl = "https://openrouter.ai/docs",
            CustomProperties = new Dictionary<string, object>
            {
                ["SupportsAttribution"] = true,
                ["SupportsModelRouting"] = true,
                ["SupportsFallbackModels"] = true,
                ["SupportsProviderRouting"] = true,
                ["SupportsPriceFiltering"] = true,
                ["SupportsZeroDataRetention"] = true,
                ["AttributionRequirements"] = "Include HttpReferer and AppName in AdditionalProperties for app rankings"
            }
        };
    }

    public ProviderValidationResult ValidateConfiguration(ProviderConfig config)
    {
        // Note: API key validation is now deferred to CreateChatClient where ISecretResolver is available
        // This method only validates config structure, not secret resolution
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return ProviderValidationResult.Failure("API key is required for OpenRouter. " +
                "Set it via the apiKey parameter, OPENROUTER_API_KEY environment variable, or configuration.");
        }

        if (string.IsNullOrEmpty(config.ModelName))
            return ProviderValidationResult.Failure("Model name is required");

        return ProviderValidationResult.Success();
    }

    /// <summary>
    /// Creates a properly configured ProviderConfig with attribution headers for OpenRouter app ranking.
    /// </summary>
    /// <param name="apiKey">Your OpenRouter API key.</param>
    /// <param name="modelName">The model to use.</param>
    /// <param name="appUrl">Your app's URL (for HTTP-Referer header).</param>
    /// <param name="appName">Your app's display name (for X-Title header).</param>
    /// <returns>A configured ProviderConfig with attribution.</returns>
    public static ProviderConfig CreateConfigWithAttribution(string apiKey, string modelName, string appUrl, string appName)
    {
        return new ProviderConfig
        {
            ProviderKey = "openrouter",
            ApiKey = apiKey,
            ModelName = modelName,
            AdditionalProperties = new Dictionary<string, object>
            {
                ["HttpReferer"] = appUrl,
                ["AppName"] = appName
            }
        };
    }

    /// <summary>
    /// Adds attribution headers to an existing ProviderConfig.
    /// </summary>
    /// <param name="config">The existing configuration.</param>
    /// <param name="appUrl">Your app's URL (for HTTP-Referer header).</param>
    /// <param name="appName">Your app's display name (for X-Title header).</param>
    /// <returns>The updated configuration.</returns>
    public static ProviderConfig WithAttribution(ProviderConfig config, string appUrl, string appName)
    {
        config.AdditionalProperties ??= new Dictionary<string, object>();
        config.AdditionalProperties["HttpReferer"] = appUrl;
        config.AdditionalProperties["AppName"] = appName;
        return config;
    }

    /// <summary>
    /// Validates an OpenRouter API key by checking if it can access the key info endpoint.
    ///    NETWORK CALL: This method makes HTTP requests and can be slow (2-5 seconds).
    /// Only use when you need live API validation. For fast builds, use ValidateConfiguration() instead.
    /// ✨ PERFORMANCE: Set OPENROUTER_SKIP_VALIDATION=true to skip validation entirely
    /// </summary>
    /// <param name="config">The provider configuration containing the API key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A validation result indicating if the key is valid.</returns>
    public async Task<ProviderValidationResult> ValidateConfigurationAsync(ProviderConfig config, CancellationToken cancellationToken = default)
    {
        var basicValidation = ValidateConfiguration(config);
        if (!basicValidation.IsValid)
            return basicValidation;

        // ✨ PERFORMANCE: Allow skipping expensive validation entirely
        if (Environment.GetEnvironmentVariable("OPENROUTER_SKIP_VALIDATION")?.ToLowerInvariant() == "true")
        {
            return ProviderValidationResult.Success(); // Validation skipped for performance
        }

        try
        {
            // Create a temporary client to test the API key
            var testClient = CreateChatClient(config) as OpenRouterChatClient;
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
            if (Environment.GetEnvironmentVariable("OPENROUTER_CHECK_CREDITS")?.ToLowerInvariant() != "false")
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
    private static AttributionInfo ExtractAttributionInfo(ProviderConfig config)
    {
        var attribution = new AttributionInfo();

        // Extract HTTP-Referer
        if (config.AdditionalProperties?.TryGetValue("HttpReferer", out var refererObj) == true)
        {
            attribution.Referer = refererObj?.ToString() ?? string.Empty;
        }
        
        // Also check for "Referer" variant (without HTTP- prefix)
        if (string.IsNullOrEmpty(attribution.Referer) && 
            config.AdditionalProperties?.TryGetValue("Referer", out var refererObj2) == true)
        {
            attribution.Referer = refererObj2?.ToString() ?? string.Empty;
        }

        // Extract X-Title / App Name
        if (config.AdditionalProperties?.TryGetValue("AppName", out var appNameObj) == true)
        {
            attribution.Title = appNameObj?.ToString() ?? string.Empty;
        }
        
        // Also check for "XTitle" or "Title" variants
        if (string.IsNullOrEmpty(attribution.Title))
        {
            if (config.AdditionalProperties?.TryGetValue("XTitle", out var xTitleObj) == true)
            {
                attribution.Title = xTitleObj?.ToString() ?? string.Empty;
            }
            else if (config.AdditionalProperties?.TryGetValue("Title", out var titleObj) == true)
            {
                attribution.Title = titleObj?.ToString() ?? string.Empty;
            }
        }

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
                       Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == null;
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
