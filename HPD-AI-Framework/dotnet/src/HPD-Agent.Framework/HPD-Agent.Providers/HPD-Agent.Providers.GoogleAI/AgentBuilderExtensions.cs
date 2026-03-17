using System;
using HPD.Agent;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.GoogleAI;

/// <summary>
/// Extension methods for AgentBuilder to configure Google AI (Gemini) as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Google AI (Gemini) as the AI provider.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="apiKey">The Google AI API key. If not provided, will attempt to resolve from environment variables</param>
    /// <param name="model">The model name (e.g., "gemini-2.0-flash", "gemini-1.5-pro")</param>
    /// <param name="configure">Optional action to configure additional Google AI-specific options</param>
    /// <param name="clientFactory">Optional factory to wrap the chat client with middleware (logging, caching, etc.)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// API Key Resolution (in priority order):
    /// 1. Explicit apiKey parameter
    /// 2. Environment variable: GOOGLE_AI_API_KEY or GEMINI_API_KEY
    /// 3. appsettings.json: "googleAI:ApiKey" or "GoogleAI:ApiKey"
    /// </para>
    /// <para>
    /// This method creates a <see cref="GoogleAIProviderConfig"/> that is:
    /// - Stored in <c>ProviderConfig.ProviderOptionsJson</c> for FFI/JSON serialization
    /// - Applied during <c>GoogleAIProvider.CreateChatClient()</c> via the registered deserializer
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same config structure directly:
    /// <code>
    /// {
    ///   "Provider": {
    ///     "ProviderKey": "google-ai",
    ///     "ModelName": "gemini-2.0-flash",
    ///     "ApiKey": "your-api-key",
    ///     "ProviderOptionsJson": "{\"maxOutputTokens\":8192,\"temperature\":0.7}"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: Basic usage with API key
    /// var agent = new AgentBuilder()
    ///     .WithGoogleAI(
    ///         apiKey: "your-api-key",
    ///         model: "gemini-2.0-flash")
    ///     .Build();
    ///
    /// // Option 2: With configuration options
    /// var agent = new AgentBuilder()
    ///     .WithGoogleAI(
    ///         apiKey: "your-api-key",
    ///         model: "gemini-1.5-pro",
    ///         configure: opts =>
    ///         {
    ///             opts.MaxOutputTokens = 8192;
    ///             opts.Temperature = 0.7;
    ///             opts.TopP = 0.95;
    ///             opts.TopK = 40;
    ///         })
    ///     .Build();
    ///
    /// // Option 3: With structured JSON output
    /// var agent = new AgentBuilder()
    ///     .WithGoogleAI(
    ///         apiKey: "your-api-key",
    ///         model: "gemini-2.0-flash",
    ///         configure: opts =>
    ///         {
    ///             opts.ResponseMimeType = "application/json";
    ///             opts.ResponseSchema = @"{
    ///                 ""type"": ""object"",
    ///                 ""properties"": {
    ///                     ""name"": { ""type"": ""string"" },
    ///                     ""age"": { ""type"": ""number"" }
    ///                 }
    ///             }";
    ///         })
    ///     .Build();
    ///
    /// // Option 4: With safety settings
    /// var agent = new AgentBuilder()
    ///     .WithGoogleAI(
    ///         apiKey: "your-api-key",
    ///         model: "gemini-2.0-flash",
    ///         configure: opts =>
    ///         {
    ///             opts.SafetySettings = new List&lt;SafetySettingConfig&gt;
    ///             {
    ///                 new() { Category = "HARM_CATEGORY_HARASSMENT", Threshold = "BLOCK_MEDIUM_AND_ABOVE" },
    ///                 new() { Category = "HARM_CATEGORY_HATE_SPEECH", Threshold = "BLOCK_MEDIUM_AND_ABOVE" }
    ///             };
    ///         })
    ///     .Build();
    ///
    /// // Option 5: With thinking config (Gemini 3+ models)
    /// var agent = new AgentBuilder()
    ///     .WithGoogleAI(
    ///         apiKey: "your-api-key",
    ///         model: "gemini-3-flash",
    ///         configure: opts =>
    ///         {
    ///             opts.IncludeThoughts = true;
    ///             opts.ThinkingLevel = "HIGH";
    ///             opts.ThinkingBudget = 5000;
    ///         })
    ///     .Build();
    ///
    /// // Option 6: Auto-resolve from environment variables
    /// // Set GOOGLE_AI_API_KEY or GEMINI_API_KEY environment variable
    /// var agent = new AgentBuilder()
    ///     .WithGoogleAI(model: "gemini-2.0-flash")
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithGoogleAI(
        this AgentBuilder builder,
        string? apiKey = null,
        string model = "gemini-2.0-flash",
        Action<GoogleAIProviderConfig>? configure = null,
        Func<IChatClient, IChatClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Google AI provider.", nameof(model));

        // Create provider config
        // Note: API key resolution is deferred to Build() time via ISecretResolver
        // The resolver will try both "google-ai:ApiKey" and "gemini:ApiKey" keys
        var providerConfig = new GoogleAIProviderConfig();

        // Allow user to configure additional options
        configure?.Invoke(providerConfig);

        // Validate configuration
        ValidateProviderConfig(providerConfig, configure);

        // Build provider config
        builder.Config.Provider = new ProviderConfig
        {
            ProviderKey = "google-ai",
            ApiKey = apiKey, // May be null - AgentBuilder.Build() will resolve via ISecretResolver
            ModelName = model
        };

        // Store the typed config
        builder.Config.Provider.SetTypedProviderConfig(providerConfig);

        // Store the client factory if provided
        if (clientFactory != null)
        {
            // Store in AdditionalProperties for the provider to retrieve during CreateChatClient
            builder.Config.Provider.AdditionalProperties ??= new System.Collections.Generic.Dictionary<string, object>();
            builder.Config.Provider.AdditionalProperties["ClientFactory"] = clientFactory;
        }

        return builder;
    }

    private static void ValidateProviderConfig(GoogleAIProviderConfig config, Action<GoogleAIProviderConfig>? configure)
    {
        // Validate Temperature range (typically 0.0 to 2.0 for most models)
        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 2))
        {
            throw new ArgumentException(
                "Temperature must be between 0 and 2.",
                nameof(configure));
        }

        // Validate TopP range
        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
        {
            throw new ArgumentException(
                "TopP must be between 0 and 1.",
                nameof(configure));
        }

        // Validate TopK (must be positive if specified)
        if (config.TopK.HasValue && config.TopK.Value < 0)
        {
            throw new ArgumentException(
                "TopK must be a positive integer.",
                nameof(configure));
        }

        // Validate CandidateCount (currently only 1 is supported)
        if (config.CandidateCount.HasValue && config.CandidateCount.Value != 1)
        {
            throw new ArgumentException(
                "CandidateCount currently only supports a value of 1.",
                nameof(configure));
        }

        // Validate ResponseMimeType with ResponseSchema
        if (!string.IsNullOrEmpty(config.ResponseSchema) &&
            config.ResponseMimeType != "application/json")
        {
            throw new ArgumentException(
                "When ResponseSchema is set, ResponseMimeType must be 'application/json'.",
                nameof(configure));
        }

        // Validate ResponseSchema and ResponseJsonSchema are mutually exclusive
        if (!string.IsNullOrEmpty(config.ResponseSchema) &&
            !string.IsNullOrEmpty(config.ResponseJsonSchema))
        {
            throw new ArgumentException(
                "ResponseSchema and ResponseJsonSchema cannot both be set. Use one or the other.",
                nameof(configure));
        }

        // Validate ResponseMimeType values
        if (!string.IsNullOrEmpty(config.ResponseMimeType))
        {
            var validMimeTypes = new[] { "text/plain", "application/json", "text/x.enum" };
            if (!Array.Exists(validMimeTypes, m => m == config.ResponseMimeType))
            {
                throw new ArgumentException(
                    "ResponseMimeType must be one of: text/plain, application/json, text/x.enum.",
                    nameof(configure));
            }
        }

        // Validate FunctionCallingMode
        if (!string.IsNullOrEmpty(config.FunctionCallingMode))
        {
            var validModes = new[] { "AUTO", "ANY", "NONE", "FUNCTION_CALLING_MODE_UNSPECIFIED" };
            if (!Array.Exists(validModes, m => m == config.FunctionCallingMode))
            {
                throw new ArgumentException(
                    "FunctionCallingMode must be one of: AUTO, ANY, NONE.",
                    nameof(configure));
            }

            // Validate AllowedFunctionNames only used with ANY mode
            if (config.AllowedFunctionNames?.Count > 0 &&
                config.FunctionCallingMode != "ANY")
            {
                throw new ArgumentException(
                    "AllowedFunctionNames can only be set when FunctionCallingMode is ANY.",
                    nameof(configure));
            }
        }

        // Validate ThinkingLevel
        if (!string.IsNullOrEmpty(config.ThinkingLevel))
        {
            var validLevels = new[] { "THINKING_LEVEL_UNSPECIFIED", "LOW", "HIGH" };
            if (!Array.Exists(validLevels, l => l == config.ThinkingLevel))
            {
                throw new ArgumentException(
                    "ThinkingLevel must be one of: THINKING_LEVEL_UNSPECIFIED, LOW, HIGH.",
                    nameof(configure));
            }
        }

        // Validate MediaResolution
        if (!string.IsNullOrEmpty(config.MediaResolution))
        {
            var validResolutions = new[] {
                "MEDIA_RESOLUTION_UNSPECIFIED",
                "MEDIA_RESOLUTION_LOW",
                "MEDIA_RESOLUTION_MEDIUM",
                "MEDIA_RESOLUTION_HIGH"
            };
            if (!Array.Exists(validResolutions, r => r == config.MediaResolution))
            {
                throw new ArgumentException(
                    "MediaResolution must be one of: MEDIA_RESOLUTION_UNSPECIFIED, MEDIA_RESOLUTION_LOW, MEDIA_RESOLUTION_MEDIUM, MEDIA_RESOLUTION_HIGH.",
                    nameof(configure));
            }
        }

        // Validate ModelRoutingPreference
        if (!string.IsNullOrEmpty(config.ModelRoutingPreference))
        {
            var validPreferences = new[] { "UNKNOWN", "PRIORITIZE_QUALITY", "BALANCED", "PRIORITIZE_COST" };
            if (!Array.Exists(validPreferences, p => p == config.ModelRoutingPreference))
            {
                throw new ArgumentException(
                    "ModelRoutingPreference must be one of: UNKNOWN, PRIORITIZE_QUALITY, BALANCED, PRIORITIZE_COST.",
                    nameof(configure));
            }
        }

        // Validate ImageOutputMimeType
        if (!string.IsNullOrEmpty(config.ImageOutputMimeType))
        {
            var validImageMimeTypes = new[] { "image/png", "image/jpeg" };
            if (!Array.Exists(validImageMimeTypes, m => m == config.ImageOutputMimeType))
            {
                throw new ArgumentException(
                    "ImageOutputMimeType must be one of: image/png, image/jpeg.",
                    nameof(configure));
            }
        }

        // Validate ImageCompressionQuality range (0-100)
        if (config.ImageCompressionQuality.HasValue &&
            (config.ImageCompressionQuality.Value < 0 || config.ImageCompressionQuality.Value > 100))
        {
            throw new ArgumentException(
                "ImageCompressionQuality must be between 0 and 100.",
                nameof(configure));
        }

        // Validate Logprobs requires ResponseLogprobs
        if (config.Logprobs.HasValue && config.ResponseLogprobs != true)
        {
            throw new ArgumentException(
                "Logprobs can only be set when ResponseLogprobs is true.",
                nameof(configure));
        }

        // Validate SafetySettings
        if (config.SafetySettings != null)
        {
            var validCategories = new[] {
                "HARM_CATEGORY_UNSPECIFIED",
                "HARM_CATEGORY_DEROGATORY",
                "HARM_CATEGORY_TOXICITY",
                "HARM_CATEGORY_VIOLENCE",
                "HARM_CATEGORY_SEXUAL",
                "HARM_CATEGORY_MEDICAL",
                "HARM_CATEGORY_DANGEROUS",
                "HARM_CATEGORY_HARASSMENT",
                "HARM_CATEGORY_HATE_SPEECH",
                "HARM_CATEGORY_SEXUALLY_EXPLICIT",
                "HARM_CATEGORY_DANGEROUS_CONTENT",
                "HARM_CATEGORY_CIVIC_INTEGRITY"
            };

            var validThresholds = new[] {
                "HARM_BLOCK_THRESHOLD_UNSPECIFIED",
                "BLOCK_LOW_AND_ABOVE",
                "BLOCK_MEDIUM_AND_ABOVE",
                "BLOCK_ONLY_HIGH",
                "BLOCK_NONE",
                "OFF"
            };

            foreach (var setting in config.SafetySettings)
            {
                if (!string.IsNullOrEmpty(setting.Category) &&
                    !Array.Exists(validCategories, c => c == setting.Category))
                {
                    throw new ArgumentException(
                        $"Invalid safety setting category: {setting.Category}",
                        nameof(configure));
                }

                if (!string.IsNullOrEmpty(setting.Threshold) &&
                    !Array.Exists(validThresholds, t => t == setting.Threshold))
                {
                    throw new ArgumentException(
                        $"Invalid safety setting threshold: {setting.Threshold}",
                        nameof(configure));
                }
            }
        }
    }
}
