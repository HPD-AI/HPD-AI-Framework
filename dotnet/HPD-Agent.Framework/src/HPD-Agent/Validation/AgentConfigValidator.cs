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
        ValidateErrorHandling(config, errors);
        ValidateCompaction(config, errors);
        ValidateCaching(config, errors);
        ValidateOperations(config, errors);
        ValidateAudio(config, errors);
        ValidateCrossConfiguration(config, errors);

        return errors;
    }

    private static void ValidateAudio(AgentConfig config, List<string> errors)
    {
        var audio = config.Audio;
        if (audio is null) return;
        if (!Enum.IsDefined(audio.InputMode)) errors.Add("Audio InputMode is invalid.");
        if (!Enum.IsDefined(audio.OutputMode)) errors.Add("Audio OutputMode is invalid.");
        if (audio.InputMode == AudioInputMode.StreamingSpeechToText &&
            config.Clients.SpeechToText is null)
            errors.Add("Streaming Audio input requires a speech-to-text client configuration.");
        if (audio.Transport is not { } transport) return;
        if (string.IsNullOrWhiteSpace(transport.ComponentInstance) || transport.ComponentInstance.Length > 128)
            errors.Add("Audio transport ComponentInstance must contain 1-128 characters.");
        if (string.IsNullOrWhiteSpace(transport.Schema) || transport.Schema.Length > 256 || transport.Version == 0)
            errors.Add("Audio transport schema identity is invalid.");
        if (!Uri.TryCreate(transport.Endpoint, UriKind.Absolute, out _))
            errors.Add("Audio transport Endpoint must be an absolute URI.");
    }

    private static void ValidateOperations(AgentConfig config, List<string> errors)
    {
        try
        {
            config.Shutdown.Validate();
            config.OperationRetention.Validate();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            errors.Add(exception.Message);
        }
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
        if (config.Compaction is null)
            return;

        if (config.Compaction.Automatic is { } automatic)
        {
            ValidateCompactionTrigger(automatic.Trigger, errors);
            ValidateCompactionSpecification(automatic.Compaction, errors);
        }
        if (config.Compaction.ForkCompaction is { } fork)
            ValidateCompactionSpecification(fork, errors);
    }

    private static void ValidateCompactionSpecification(CompactionSpecification specification, List<string> errors)
    {
        switch (specification.Point)
        {
            case CompactAtMessage { MessageId.Length: 0 }:
                errors.Add("CompactAtMessage.MessageId is required.");
                break;
            case CompactAtTurn { TurnId.Length: 0 }:
                errors.Add("CompactAtTurn.TurnId is required.");
                break;
        }

        switch (specification.Preservation)
        {
            case PreservePreviousTurns { Count: < 0 }:
                errors.Add("PreservePreviousTurns.Count must be zero or greater.");
                break;
            case PreservePreviousUserMessages { Limit: PreviousItemCountLimit { Count: < 0 } }:
                errors.Add("PreviousItemCountLimit.Count must be zero or greater.");
                break;
            case PreservePreviousUserMessages { Limit: PreviousTokenBudgetLimit { Tokens: < 0 } }:
                errors.Add("PreviousTokenBudgetLimit.Tokens must be zero or greater.");
                break;
        }
    }

    private static void ValidateCompactionTrigger(CompactionTrigger trigger, List<string> errors)
    {
        switch (trigger)
        {
            case TurnCountCompactionTrigger { Turns: <= 0 }:
                errors.Add("TurnCountCompactionTrigger.Turns must be greater than zero.");
                break;
            case InputTokenCompactionTrigger { InputTokens: <= 0 }:
                errors.Add("InputTokenCompactionTrigger.InputTokens must be greater than zero.");
                break;
            case ContextPercentageCompactionTrigger percentage
                when percentage.TotalInputTokens <= 0 || percentage.Percentage is <= 0 or >= 1:
                errors.Add("ContextPercentageCompactionTrigger requires positive total input tokens and a percentage between zero and one.");
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

    private static bool HasReasonableResourceLimits(AgentConfig config)
    {
        // Check if the combination of settings might cause issues
        var maxFunctionCalls = config.MaxAgenticIterations;
        var maxHistory = config.Compaction?.Automatic?.Compaction.Preservation switch
        {
            PreservePreviousTurns turns => turns.Count,
            PreservePreviousUserMessages { Limit: PreviousItemCountLimit count } => count.Count,
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
