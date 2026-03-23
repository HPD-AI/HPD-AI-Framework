using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HPD.Agent.Validation;

/// <summary>
/// Validates AgentConfig objects and throws ValidationException if invalid.
/// </summary>
public static class AgentConfigValidator
{
    /// <summary>
    /// Validates the configuration and throws if invalid.
    /// </summary>
    public static void ValidateAndThrow(AgentConfig config)
    {
        var errors = Validate(config);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    /// <summary>
    /// Validates the configuration and returns a list of error messages.
    /// </summary>
    public static List<string> Validate(AgentConfig config)
    {
        var errors = new List<string>();

        // Basic configuration validation
        ValidateName(config, errors);
        ValidateMaxAgenticIterations(config, errors);
        ValidateProvider(config, errors);
        ValidateMcp(config, errors);
        ValidateErrorHandling(config, errors);
        ValidateHistoryReduction(config, errors);
        ValidateCaching(config, errors);
        ValidateCrossConfiguration(config, errors);

        return errors;
    }

    private static void ValidateName(AgentConfig config, List<string> errors)
    {
        if (string.IsNullOrEmpty(config.Name))
        {
            errors.Add("Agent name must not be empty.");
        }
        else if (config.Name.Length < 1 || config.Name.Length > 100)
        {
            errors.Add("Agent name must be between 1 and 100 characters.");
        }
    }

    private static void ValidateMaxAgenticIterations(AgentConfig config, List<string> errors)
    {
        if (config.MaxAgenticIterations <= 0 || config.MaxAgenticIterations > 50)
        {
            errors.Add("MaxFunctionCallTurns must be between 1 and 50.");
        }
    }

    private static void ValidateProvider(AgentConfig config, List<string> errors)
    {
        if (config.Provider == null)
        {
            errors.Add("A provider must be configured for the agent.");
            return;
        }

        // Model name validation
        if (string.IsNullOrEmpty(config.Provider.ModelName))
        {
            errors.Add("Provider model name must be specified.");
        }

        // Provider-specific validation
        var providerKey = config.Provider.ProviderKey?.ToLowerInvariant();

        if (providerKey == "azureopenai")
        {
            if (string.IsNullOrEmpty(config.Provider.Endpoint))
            {
                errors.Add("Azure OpenAI requires an endpoint URL.");
            }
            else if (!IsValidUri(config.Provider.Endpoint))
            {
                errors.Add("Azure OpenAI endpoint must be a valid URI.");
            }
        }

        if (providerKey == "ollama")
        {
            if (!string.IsNullOrEmpty(config.Provider.ModelName) && config.Provider.ModelName.Contains('/'))
            {
                errors.Add("Ollama model name should not contain '/' characters.");
            }
        }

        // Generic endpoint validation
        if (!string.IsNullOrEmpty(config.Provider.Endpoint) && !IsValidUri(config.Provider.Endpoint))
        {
            errors.Add("Provider endpoint must be a valid URI.");
        }

        // Model combination validation
        if (!IsValidProviderModelCombination(config))
        {
            errors.Add("The specified model is not supported by the selected provider.");
        }
    }

    private static void ValidateMcp(AgentConfig config, List<string> errors)
    {
        if (config.Mcp != null && !string.IsNullOrEmpty(config.Mcp.ManifestPath))
        {
            if (!IsValidPath(config.Mcp.ManifestPath))
            {
                errors.Add("MCP ManifestPath must be a valid file path.");
            }
        }
    }

    private static void ValidateErrorHandling(AgentConfig config, List<string> errors)
    {
        if (config.ErrorHandling != null)
        {
            if (config.ErrorHandling.MaxRetries < 0 || config.ErrorHandling.MaxRetries > 10)
            {
                errors.Add("ErrorHandling MaxRetries must be between 0 and 10.");
            }
        }
    }

    private static void ValidateHistoryReduction(AgentConfig config, List<string> errors)
    {
        if (config.HistoryReduction?.Enabled != true)
            return;

        var hr = config.HistoryReduction;

        // Percentage-based validation
        if (hr.TokenBudgetTriggerPercentage.HasValue)
        {
            if (hr.ContextWindowSize == null)
            {
                errors.Add("ContextWindowSize must be set when using TokenBudgetTriggerPercentage.");
            }

            if (hr.TokenBudgetTriggerPercentage.Value <= 0 || hr.TokenBudgetTriggerPercentage.Value >= 1)
            {
                errors.Add("TokenBudgetTriggerPercentage must be between 0 and 1 (e.g., 0.7 for 70%).");
            }

            if (hr.TokenBudgetPreservePercentage <= 0 || hr.TokenBudgetPreservePercentage >= 1)
            {
                errors.Add("TokenBudgetPreservePercentage must be between 0 and 1 (e.g., 0.3 for 30%).");
            }

            if (hr.ContextWindowSize.HasValue)
            {
                if (hr.ContextWindowSize.Value <= 1000 || hr.ContextWindowSize.Value > 2000000)
                {
                    errors.Add("ContextWindowSize must be between 1,000 and 2,000,000 tokens.");
                }
            }

            // Ensure trigger percentage is larger than preserve percentage
            if (hr.TokenBudgetTriggerPercentage <= hr.TokenBudgetPreservePercentage)
            {
                errors.Add("TokenBudgetTriggerPercentage must be larger than TokenBudgetPreservePercentage.");
            }
        }

        // Count validation
        if (hr.TargetCount <= 1 || hr.TargetCount > 1000)
        {
            errors.Add("TargetCount must be between 2 and 1,000.");
        }

        if (hr.SummarizationThreshold.HasValue)
        {
            if (hr.SummarizationThreshold.Value < 0 || hr.SummarizationThreshold.Value > 100)
            {
                errors.Add("SummarizationThreshold must be between 0 and 100.");
            }
        }
    }

    private static void ValidateCaching(AgentConfig config, List<string> errors)
    {
        if (config.Caching?.Enabled != true)
            return;

        if (config.Caching.CacheExpiration == null)
        {
            errors.Add("CachingConfig.CacheExpiration must be set when caching is enabled.");
        }
        else if (config.Caching.CacheExpiration <= TimeSpan.Zero)
        {
            errors.Add("CachingConfig.CacheExpiration must be greater than zero.");
        }
        else if (config.Caching.CacheExpiration >= TimeSpan.FromDays(7))
        {
            errors.Add("CachingConfig.CacheExpiration should not exceed 7 days (prevents stale cache).");
        }
    }

    private static void ValidateCrossConfiguration(AgentConfig config, List<string> errors)
    {
        if (!HasReasonableResourceLimits(config))
        {
            errors.Add("Resource limits (MaxTokens, MaxFunctionCallTurns) may be too high for stable operation.");
        }
    }

    #region Helper Methods

    private static bool IsValidUri(string? uri)
    {
        return !string.IsNullOrEmpty(uri) && Uri.TryCreate(uri, UriKind.Absolute, out _);
    }

    private static bool IsValidPath(string path)
    {
        try
        {
            // Basic path validation - avoid path traversal and null characters
            return !string.IsNullOrWhiteSpace(path) &&
                   path.IndexOfAny(Path.GetInvalidPathChars()) == -1 &&
                   !path.Contains("..") &&
                   path.Length < 260; // Windows path limit
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidProviderModelCombination(AgentConfig config)
    {
        if (config.Provider == null) return true;

        return config.Provider.ProviderKey?.ToLowerInvariant() switch
        {
            "openai" => IsValidOpenAIModel(config.Provider.ModelName),
            "openrouter" => IsValidOpenRouterModel(config.Provider.ModelName),
            "azureopenai" => IsValidAzureModel(config.Provider.ModelName),
            "ollama" => IsValidOllamaModel(config.Provider.ModelName),
            _ => true // Unknown providers are allowed
        };
    }

    private static bool IsValidOpenAIModel(string? modelName)
    {
        // OpenAI now has many models; accept any non-empty model name
        return !string.IsNullOrEmpty(modelName);
    }

    private static bool IsValidOpenRouterModel(string? modelName)
    {
        // OpenRouter accepts many model formats, be more lenient
        return !string.IsNullOrEmpty(modelName) && modelName.Length > 3;
    }

    private static bool IsValidAzureModel(string? modelName)
    {
        // Azure model names are deployment names, can be anything
        return !string.IsNullOrEmpty(modelName);
    }

    private static bool IsValidOllamaModel(string? modelName)
    {
        // Ollama models typically don't have slashes in local names
        return !string.IsNullOrEmpty(modelName) &&
               modelName.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.');
    }

    private static bool HasReasonableResourceLimits(AgentConfig config)
    {
        // Check if the combination of settings might cause issues
        var maxFunctionCalls = config.MaxAgenticIterations;
        var maxHistory = config.HistoryReduction?.TargetCount ?? 20;

        // Warn if total potential token usage is very high
        var estimatedMaxTokens = (maxHistory * 500) + (maxFunctionCalls * 200);
        return estimatedMaxTokens < 200000; // Reasonable upper limit
    }

    #endregion
}

/// <summary>
/// Exception thrown when validation fails.
/// </summary>
public class ValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ValidationException(IReadOnlyList<string> errors)
        : base(FormatMessage(errors))
    {
        Errors = errors;
    }

    private static string FormatMessage(IReadOnlyList<string> errors)
    {
        return $"Validation failed with {errors.Count} error(s):{Environment.NewLine}" +
               string.Join(Environment.NewLine, errors.Select(e => $"  - {e}"));
    }
}
