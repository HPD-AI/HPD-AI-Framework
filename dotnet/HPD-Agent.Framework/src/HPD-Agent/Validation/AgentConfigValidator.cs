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
        ValidateCompaction(config, errors);
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
        var chatConfig = config.ResolveClientConfig(Providers.ProviderClientFamily.Chat);
        if (chatConfig == null)
        {
            return;
        }

        // Provider/model are optional at setup time. If one is partially configured,
        // runtime can complete it via AgentRunConfig.
        if (string.IsNullOrEmpty(chatConfig.ProviderKey) && string.IsNullOrEmpty(chatConfig.ModelName))
        {
            return;
        }

        // Provider-specific validation
        var providerKey = chatConfig.ProviderKey?.ToLowerInvariant();

        if (providerKey == "azureopenai" && !string.IsNullOrEmpty(chatConfig.Endpoint))
        {
            if (!IsValidUri(chatConfig.Endpoint))
            {
                errors.Add("Azure OpenAI endpoint must be a valid URI.");
            }
        }

        if (providerKey == "ollama")
        {
            if (!string.IsNullOrEmpty(chatConfig.ModelName) && chatConfig.ModelName.Contains('/'))
            {
                errors.Add("Ollama model name should not contain '/' characters.");
            }
        }

        // Generic endpoint validation
        if (!string.IsNullOrEmpty(chatConfig.Endpoint) && !IsValidUri(chatConfig.Endpoint))
        {
            errors.Add("Provider endpoint must be a valid URI.");
        }

        // Model combination validation
        if (!string.IsNullOrEmpty(chatConfig.ModelName) && !IsValidProviderModelCombination(chatConfig))
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

        if (config.Mcp != null &&
            !string.IsNullOrEmpty(config.Mcp.ManifestPath) &&
            !string.IsNullOrEmpty(config.Mcp.ManifestContent))
        {
            errors.Add("MCP ManifestPath and ManifestContent cannot both be set.");
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

    private static void ValidateCompaction(AgentConfig config, List<string> errors)
    {
        if (config.Compaction?.Enabled != true)
            return;

        var hr = config.Compaction;

        ValidateCompactionStrategy(hr.Strategy, errors);
        if (hr.ForkCompaction?.Strategy is { } forkStrategy)
            ValidateCompactionStrategy(forkStrategy, errors);

        ValidateCompactionTrigger(hr.Trigger, errors);
        ValidateHistoryRetention(hr.Retention, errors);
    }

    private static void ValidateCompactionStrategy(CompactionStrategyOptions strategy, List<string> errors)
    {
        switch (strategy)
        {
            case MessageCountingCompactionOptions messageCounting:
                if (messageCounting.PreserveRecentUserTurnCount <= 0 || messageCounting.PreserveRecentUserTurnCount > 1000)
                    errors.Add("MessageCountingCompactionOptions.PreserveRecentUserTurnCount must be between 1 and 1,000.");
                break;

            case SummarizingCompactionOptions summarizing:
                if (summarizing.PreserveRecentUserTurnCount <= 0 || summarizing.PreserveRecentUserTurnCount > 1000)
                    errors.Add("SummarizingCompactionOptions.PreserveRecentUserTurnCount must be between 1 and 1,000.");

                if (summarizing.ResummarizeAfterNewMessages < 0 || summarizing.ResummarizeAfterNewMessages > 100)
                    errors.Add("SummarizingCompactionOptions.ResummarizeAfterNewMessages must be between 0 and 100.");

                if (summarizing.Memory.RecentUserMessageTokenBudget < 0)
                    errors.Add("SummaryMemoryOptions.RecentUserMessageTokenBudget must be zero or greater.");
                break;

            default:
                errors.Add($"Unknown compaction strategy option type: {strategy.GetType().Name}.");
                break;
        }
    }

    private static void ValidateCompactionTrigger(CompactionTriggerOptions trigger, List<string> errors)
    {
        switch (trigger)
        {
            case CountCompactionTriggerOptions count:
                if (count.TargetCount <= 1 || count.TargetCount > 1000)
                    errors.Add("CountCompactionTriggerOptions.TargetCount must be between 2 and 1,000.");

                if (count.Threshold < 0 || count.Threshold > 100)
                    errors.Add("CountCompactionTriggerOptions.Threshold must be between 0 and 100.");
                break;

            case ContextWindowCompactionTriggerOptions contextWindow:
                if (contextWindow.ContextWindowSize is <= 1000 or > 2000000)
                    errors.Add("ContextWindowCompactionTriggerOptions.ContextWindowSize must be between 1,000 and 2,000,000 tokens.");

                switch (contextWindow.ThresholdMode)
                {
                    case ContextWindowCompactionThresholdMode.Percentage:
                        if (contextWindow.TriggerPercentage <= 0 || contextWindow.TriggerPercentage >= 1)
                            errors.Add("ContextWindowCompactionTriggerOptions.TriggerPercentage must be between 0 and 1.");
                        break;

                    case ContextWindowCompactionThresholdMode.TokenCount:
                        if (contextWindow.TriggerTokenCount is null or <= 0)
                            errors.Add("ContextWindowCompactionTriggerOptions.TriggerTokenCount must be greater than zero when ThresholdMode is TokenCount.");
                        break;

                    default:
                        errors.Add("ContextWindowCompactionTriggerOptions.ThresholdMode is invalid.");
                        break;
                }
                break;

            case CompositeCompactionTriggerOptions composite:
                if (composite.AnyOf.Count == 0)
                    errors.Add("CompositeCompactionTriggerOptions.AnyOf must include at least one trigger.");

                foreach (var child in composite.AnyOf)
                    ValidateCompactionTrigger(child, errors);
                break;

            default:
                errors.Add($"Unknown compaction trigger option type: {trigger.GetType().Name}.");
                break;
        }
    }

    private static void ValidateHistoryRetention(CompactionRetentionOptions retention, List<string> errors)
    {
        switch (retention)
        {
            case PreserveThreadHistoryOptions:
                break;
            case CompactThreadHistoryOptions compact:
                ValidateCompactionBoundary(compact.Boundary, errors);
                break;
            default:
                errors.Add($"Unknown history retention option type: {retention.GetType().Name}.");
                break;
        }
    }

    private static void ValidateCompactionBoundary(CompactionBoundaryOptions boundary, List<string> errors)
    {
        switch (boundary)
        {
            case IncludePreviousMessagesBoundaryOptions previous when previous.Count < 0:
                errors.Add("IncludePreviousMessagesBoundaryOptions.Count must be zero or greater.");
                break;
            case CompositeCompactionBoundaryOptions composite:
                if (composite.Policies.Count == 0)
                    errors.Add("CompositeCompactionBoundaryOptions.Policies must include at least one boundary policy.");

                foreach (var child in composite.Policies)
                    ValidateCompactionBoundary(child, errors);
                break;
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

    private static bool IsValidProviderModelCombination(ClientProviderConfig config)
    {
        return config.ProviderKey?.ToLowerInvariant() switch
        {
            "openai" => IsValidOpenAIModel(config.ModelName),
            "openrouter" => IsValidOpenRouterModel(config.ModelName),
            "azureopenai" => IsValidAzureModel(config.ModelName),
            "ollama" => IsValidOllamaModel(config.ModelName),
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
        var maxHistory = config.Compaction?.Strategy switch
        {
            MessageCountingCompactionOptions messageCounting => messageCounting.PreserveRecentUserTurnCount,
            SummarizingCompactionOptions summarizing => summarizing.PreserveRecentUserTurnCount,
            _ => 20
        };

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
        return $"Validation failed with {errors.Count} error(s):{System.Environment.NewLine}" +
               string.Join(System.Environment.NewLine, errors.Select(e => $"  - {e}"));
    }
}
