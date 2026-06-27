using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.OpenAICompatible;

public static class OpenAICompatibleChatOptionsDefaults
{
    public static ChatOptions Apply(
        string defaultModelId,
        OpenAICompatibleProviderConfig? config,
        ChatOptions? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultModelId);

        if (options is null)
        {
            return CreateDefaultOptions(defaultModelId, config);
        }

        return new ChatOptions
        {
            ModelId = string.IsNullOrWhiteSpace(options.ModelId) ? defaultModelId : options.ModelId,
            Instructions = options.Instructions,
            Tools = options.Tools,
            MaxOutputTokens = options.MaxOutputTokens ?? config?.MaxOutputTokens,
            Temperature = options.Temperature ?? config?.Temperature,
            TopP = options.TopP ?? config?.TopP,
            FrequencyPenalty = options.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty,
            StopSequences = options.StopSequences ?? config?.StopSequences,
            ResponseFormat = options.ResponseFormat ?? CreateResponseFormat(config?.ResponseFormat),
            Seed = options.Seed ?? config?.Seed,
            ToolMode = options.ToolMode ?? CreateToolMode(config?.ToolChoice),
            AdditionalProperties = options.AdditionalProperties,
            RawRepresentationFactory = options.RawRepresentationFactory
        };
    }

    public static void Validate(OpenAICompatibleProviderConfig config, IList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(errors);

        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 2))
        {
            errors.Add("Temperature must be between 0 and 2");
        }

        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
        {
            errors.Add("TopP must be between 0 and 1");
        }

        if (config.MaxOutputTokens.HasValue && config.MaxOutputTokens.Value <= 0)
        {
            errors.Add("MaxOutputTokens must be greater than 0");
        }

        if (config.StopSequences is { Count: > 0 })
        {
            foreach (var stopSequence in config.StopSequences)
            {
                if (string.IsNullOrEmpty(stopSequence))
                {
                    errors.Add("StopSequences cannot contain empty values");
                }
            }
        }

        if (config.ResponseFormat is { Length: > 0 } responseFormat &&
            !string.Equals(responseFormat, "text", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(responseFormat, "json_object", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ResponseFormat must be one of: text, json_object");
        }

        if (config.ToolChoice is { Length: > 0 } toolChoice &&
            !string.Equals(toolChoice, "auto", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(toolChoice, "none", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(toolChoice, "required", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ToolChoice must be one of: auto, none, required");
        }
    }

    private static ChatOptions CreateDefaultOptions(
        string defaultModelId,
        OpenAICompatibleProviderConfig? config)
        => new()
        {
            ModelId = defaultModelId,
            MaxOutputTokens = config?.MaxOutputTokens,
            Temperature = config?.Temperature,
            TopP = config?.TopP,
            StopSequences = config?.StopSequences,
            ResponseFormat = CreateResponseFormat(config?.ResponseFormat),
            Seed = config?.Seed,
            ToolMode = CreateToolMode(config?.ToolChoice)
        };

    private static ChatResponseFormat? CreateResponseFormat(string? responseFormat)
        => responseFormat?.ToLowerInvariant() switch
        {
            "text" => ChatResponseFormat.Text,
            "json_object" => ChatResponseFormat.Json,
            _ => null
        };

    private static ChatToolMode? CreateToolMode(string? toolChoice)
        => toolChoice?.ToLowerInvariant() switch
        {
            "auto" => ChatToolMode.Auto,
            "none" => ChatToolMode.None,
            "required" => ChatToolMode.RequireAny,
            _ => null
        };
}

