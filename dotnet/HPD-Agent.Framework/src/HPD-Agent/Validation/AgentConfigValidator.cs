using System;
using System.Collections.Generic;
using System.Linq;
using HPD.Agent.MCP;

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
        => ValidateAndThrow(config, hostLimits: null);

    /// <summary>
    /// Validates the configuration against optional host-owned limits and throws if invalid.
    /// </summary>
    public static void ValidateAndThrow(
        AgentConfig config,
        AgentConfigValidationLimits? hostLimits)
    {
        var errors = Validate(config, hostLimits);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    /// <summary>
    /// Validates the configuration and returns a list of error messages.
    /// </summary>
    public static List<string> Validate(AgentConfig config)
        => Validate(config, hostLimits: null);

    /// <summary>
    /// Validates the configuration against optional host-owned limits.
    /// </summary>
    public static List<string> Validate(
        AgentConfig config,
        AgentConfigValidationLimits? hostLimits)
    {
        var errors = new List<string>();
        if (hostLimits is not null)
            ValidateHostLimits(hostLimits, errors);

        // Basic configuration validation
        ValidateName(config, hostLimits, errors);
        ValidateMaxAgenticIterations(config, hostLimits, errors);
        ValidateResilience(config, hostLimits, errors);
        ValidatePrivacy(config, hostLimits, errors);
        ValidateObservability(config, errors);
        ValidateCompaction(config, errors);
        if (config.Goals is { } goals) errors.AddRange(goals.Validate());
        ValidateOperations(config, errors);
        if (config.ToolOutputs is null)
            errors.Add("ToolOutputs is required.");
        else
            errors.AddRange(config.ToolOutputs.Validate("ToolOutputs"));
        ValidateAudio(config, hostLimits, errors);
        ValidateMcp(config, errors);

        return errors;
    }

    private static void ValidateHostLimits(
        AgentConfigValidationLimits limits,
        List<string> errors)
    {
        if (limits.MaximumAgentNameLength is < 1)
            errors.Add("ValidationLimits.MaximumAgentNameLength must be positive.");
        if (limits.MaximumAgenticIterations is < 1)
            errors.Add("ValidationLimits.MaximumAgenticIterations must be positive.");
        if (limits.MaximumRetries is < 0)
            errors.Add("ValidationLimits.MaximumRetries must not be negative.");
        if (limits.MaximumConsecutiveCalls is < 1)
            errors.Add("ValidationLimits.MaximumConsecutiveCalls must be positive.");
        if (limits.MaximumConsecutiveErrors is < 1)
            errors.Add("ValidationLimits.MaximumConsecutiveErrors must be positive.");
        if (limits.MaximumTotalErrors is < 1)
            errors.Add("ValidationLimits.MaximumTotalErrors must be positive.");
        if (limits.MaximumExtensionNameLength is < 1)
            errors.Add("ValidationLimits.MaximumExtensionNameLength must be positive.");
        if (limits.MaximumAudioComponentInstanceLength is < 1)
            errors.Add("ValidationLimits.MaximumAudioComponentInstanceLength must be positive.");
        if (limits.MaximumAudioSchemaLength is < 1)
            errors.Add("ValidationLimits.MaximumAudioSchemaLength must be positive.");
    }

    private static void ValidateMcp(AgentConfig config, List<string> errors)
    {
        if (config.Mcp is null)
            return;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var server in config.Mcp.Servers)
        {
            try
            {
                server.Validate();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                errors.Add($"{server.SourcePath ?? "mcp.servers"}: {exception.Message}");
            }
            if (!string.IsNullOrWhiteSpace(server.Name) && !names.Add(server.Name))
                errors.Add($"{server.SourcePath ?? "mcp.servers"}.name: Inline MCP server '{server.Name}' is declared more than once.");
        }

        foreach (var manifest in config.Mcp.Manifests)
        {
            if (string.IsNullOrWhiteSpace(manifest.Path))
                errors.Add($"{manifest.SourcePath ?? "mcp.manifests"}.path: An MCP manifest path is required.");
        }
    }

    private static void ValidateAudio(
        AgentConfig config,
        AgentConfigValidationLimits? hostLimits,
        List<string> errors)
    {
        var audio = config.Audio;
        if (audio is null) return;
        if (!Enum.IsDefined(audio.InputMode)) errors.Add("Audio InputMode is invalid.");
        if (!Enum.IsDefined(audio.OutputMode)) errors.Add("Audio OutputMode is invalid.");
        if (audio.InputMode == AudioInputMode.StreamingSpeechToText &&
            config.Clients.SpeechToText is null)
            errors.Add("Streaming Audio input requires a speech-to-text client configuration.");
        if (audio.Transport is not { } transport) return;
        if (string.IsNullOrWhiteSpace(transport.ComponentInstance))
            errors.Add("Audio transport ComponentInstance must not be empty.");
        else if (hostLimits?.MaximumAudioComponentInstanceLength is { } componentLimit &&
                 transport.ComponentInstance.Length > componentLimit)
            errors.Add($"Audio transport ComponentInstance exceeds host limit of {componentLimit} characters.");
        if (string.IsNullOrWhiteSpace(transport.Schema) || transport.Version == 0)
            errors.Add("Audio transport schema identity is invalid.");
        else if (hostLimits?.MaximumAudioSchemaLength is { } schemaLimit &&
                 transport.Schema.Length > schemaLimit)
            errors.Add($"Audio transport Schema exceeds host limit of {schemaLimit} characters.");
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

    private static void ValidateName(
        AgentConfig config,
        AgentConfigValidationLimits? hostLimits,
        List<string> errors)
    {
        if (string.IsNullOrEmpty(config.Name))
        {
            errors.Add("Agent name must not be empty.");
        }
        else if (hostLimits?.MaximumAgentNameLength is { } nameLimit &&
                 config.Name.Length > nameLimit)
        {
            errors.Add($"Agent name exceeds host limit of {nameLimit} characters.");
        }
    }

    private static void ValidateMaxAgenticIterations(
        AgentConfig config,
        AgentConfigValidationLimits? hostLimits,
        List<string> errors)
    {
        if (config.MaxAgenticIterations <= 0)
            errors.Add("MaxFunctionCallTurns must be greater than zero.");
        else if (hostLimits?.MaximumAgenticIterations is { } iterationLimit &&
                 config.MaxAgenticIterations > iterationLimit)
            errors.Add($"MaxFunctionCallTurns exceeds host limit of {iterationLimit}.");
    }

    private static void ValidateResilience(
        AgentConfig config,
        AgentConfigValidationLimits? hostLimits,
        List<string> errors)
    {
        if (config.Resilience is not { } resilience)
            return;
        if (resilience.Retry is { } retry)
        {
            ValidateRetryPolicy(
                retry.ModelRetries,
                "resilience.retry.modelRetries",
                hostLimits?.MaximumRetries,
                errors);
            ValidateRetryPolicy(
                retry.FunctionRetries,
                "resilience.retry.functionRetries",
                hostLimits?.MaximumRetries,
                errors);
            ValidateRetryExclusions(retry.FunctionRetries.Exclude,
                "resilience.retry.functionRetries.exclude", errors);
            ValidateExtensionName(
                retry.ModelCustomRetryStrategy,
                "resilience.retry.modelCustomRetryStrategy",
                hostLimits?.MaximumExtensionNameLength,
                errors);
            ValidateExtensionName(
                retry.FunctionCustomRetryStrategy,
                "resilience.retry.functionCustomRetryStrategy",
                hostLimits?.MaximumExtensionNameLength,
                errors);
        }
        if (resilience.CircuitBreaker is { } circuitBreaker)
        {
            if (circuitBreaker.MaxConsecutiveCalls < 1)
                errors.Add("resilience.circuitBreaker.maxConsecutiveCalls must be greater than zero.");
            else if (hostLimits?.MaximumConsecutiveCalls is { } callLimit &&
                     circuitBreaker.MaxConsecutiveCalls > callLimit)
                errors.Add($"resilience.circuitBreaker.maxConsecutiveCalls exceeds host limit of {callLimit}.");
        }
        if (resilience.ConsecutiveErrors is { } consecutive)
        {
            if (consecutive.MaxConsecutiveErrors < 1)
                errors.Add("resilience.consecutiveErrors.maxConsecutiveErrors must be greater than zero.");
            else if (hostLimits?.MaximumConsecutiveErrors is { } consecutiveLimit &&
                     consecutive.MaxConsecutiveErrors > consecutiveLimit)
                errors.Add($"resilience.consecutiveErrors.maxConsecutiveErrors exceeds host limit of {consecutiveLimit}.");
        }
        if (resilience.TotalErrors is { } total)
        {
            if (total.MaxTotalErrors < 1)
                errors.Add("resilience.totalErrors.maxTotalErrors must be greater than zero.");
            else if (hostLimits?.MaximumTotalErrors is { } totalLimit &&
                     total.MaxTotalErrors > totalLimit)
                errors.Add($"resilience.totalErrors.maxTotalErrors exceeds host limit of {totalLimit}.");
            ValidateExtensionName(
                total.CustomErrorDetector,
                "resilience.totalErrors.customErrorDetector",
                hostLimits?.MaximumExtensionNameLength,
                errors);
        }
        if (resilience.FunctionTimeout is { } timeout && timeout <= TimeSpan.Zero)
            errors.Add("resilience.functionTimeout must be positive.");
    }

    private static void ValidatePrivacy(
        AgentConfig config,
        AgentConfigValidationLimits? hostLimits,
        List<string> errors)
    {
        if (config.Privacy?.PII is not { } pii)
            return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < pii.CustomDetectors.Count; index++)
        {
            var detector = pii.CustomDetectors[index];
            var path = $"privacy.pii.customDetectors[{index}]";
            if (string.IsNullOrWhiteSpace(detector.Name))
                errors.Add($"{path}.name must not be empty.");
            else if (!names.Add(detector.Name))
                errors.Add($"{path}.name duplicates detector '{detector.Name}'.");
            if (string.IsNullOrWhiteSpace(detector.Pattern))
            {
                errors.Add($"{path}.pattern must not be empty.");
                continue;
            }
            try
            {
                _ = new System.Text.RegularExpressions.Regex(detector.Pattern);
            }
            catch (ArgumentException)
            {
                errors.Add($"{path}.pattern is not a valid regular expression.");
            }
        }
        ValidateExtensionName(
            pii.ExternalDetector,
            "privacy.pii.externalDetector",
            hostLimits?.MaximumExtensionNameLength,
            errors);
    }

    private static void ValidateObservability(AgentConfig config, List<string> errors)
    {
        if (config.Observability?.Logging is not { } logging)
            return;
        if (logging.MaxStringLength < 0)
            errors.Add("observability.logging.maxStringLength must not be negative.");
        if (logging.MaxLoggedMessages < 0)
            errors.Add("observability.logging.maxLoggedMessages must not be negative.");
        if (logging.MessagePreviewLength < 0)
            errors.Add("observability.logging.messagePreviewLength must not be negative.");
    }

    private static void ValidateRetryPolicy(
        RetryPolicyConfig policy,
        string path,
        int? maximumRetries,
        List<string> errors)
    {
        if (policy.MaxRetries < 0)
            errors.Add($"{path}.MaxRetries must not be negative.");
        else if (maximumRetries is { } retryLimit && policy.MaxRetries > retryLimit)
            errors.Add($"{path}.MaxRetries exceeds host limit of {retryLimit}.");
        if (policy.RetryDelay < TimeSpan.Zero)
            errors.Add($"{path}.RetryDelay must not be negative.");
        if (policy.MaxRetryDelay < TimeSpan.Zero)
            errors.Add($"{path}.MaxRetryDelay must not be negative.");
        if (policy.BackoffMultiplier < 1)
            errors.Add($"{path}.BackoffMultiplier must be at least 1.");

        foreach (var (category, retries) in policy.MaxRetriesByCategory ?? [])
        {
            if (retries < 0)
                errors.Add($"{path}.MaxRetriesByCategory.{category} must not be negative.");
            else if (maximumRetries is { } retryLimit && retries > retryLimit)
                errors.Add($"{path}.MaxRetriesByCategory.{category} exceeds host limit of {retryLimit}.");
        }
    }

    private static void ValidateRetryExclusions(
        RetryExclusionConfig exclusions,
        string path,
        List<string> errors)
    {
        ValidateExactSelectors(exclusions.Harnesses, path + ".harnesses", errors);
        ValidateExactSelectors(exclusions.Functions, path + ".functions", errors);

        var actions = new HashSet<(string Function, string Action)>();
        for (var index = 0; index < exclusions.Actions.Count; index++)
        {
            var selector = exclusions.Actions[index];
            var actionPath = $"{path}.actions[{index}]";
            if (string.IsNullOrWhiteSpace(selector.Function))
                errors.Add($"{actionPath}.function must not be empty.");
            if (string.IsNullOrWhiteSpace(selector.Action))
                errors.Add($"{actionPath}.action must not be empty.");
            if (!string.IsNullOrWhiteSpace(selector.Function) &&
                !string.IsNullOrWhiteSpace(selector.Action) &&
                !actions.Add((selector.Function, selector.Action)))
            {
                errors.Add($"{actionPath} duplicates function '{selector.Function}' action '{selector.Action}'.");
            }
        }
    }

    private static void ValidateExactSelectors(
        IEnumerable<string> selectors,
        string path,
        List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selector in selectors)
        {
            if (string.IsNullOrWhiteSpace(selector))
                errors.Add($"{path} must not contain an empty selector.");
            else if (!seen.Add(selector))
                errors.Add($"{path} contains duplicate selector '{selector}'.");
        }
    }

    private static void ValidateExtensionName(
        string? name,
        string path,
        int? maximumLength,
        List<string> errors)
    {
        if (name is null)
            return;
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add($"{path} must not be empty.");
            return;
        }
        if (maximumLength is { } limit && name.Length > limit)
            errors.Add($"{path} exceeds host limit of {limit} characters.");
        if (name.Any(char.IsControl))
            errors.Add($"{path} must not contain control characters.");
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
