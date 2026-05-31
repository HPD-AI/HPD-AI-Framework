using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using OllamaSharp;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Ollama;

/// <summary>
/// Ollama provider implementation for local and remote Ollama instances.
/// </summary>
/// <remarks>
/// <para>
/// This provider uses the OllamaSharp library to communicate with Ollama servers.
/// Supports all Ollama models and configuration options.
/// </para>
/// <para>
/// Endpoint formats:
/// - Local: http://localhost:11434 (default)
/// - Remote: http://your-server:11434
/// </para>
/// <para>
/// Provider-specific options are stored in ProviderOptionsJson and validated through
/// OllamaProviderConfig.
/// </para>
/// </remarks>
internal class OllamaProvider : IChatClientProvider
{
    public string ProviderKey => "ollama";
    public string DisplayName => "Ollama";

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
    {
        // Resolve endpoint - defaults to localhost if not provided
        var endpoint = string.IsNullOrEmpty(config.Endpoint)
            ? new Uri("http://localhost:11434")
            : new Uri(config.Endpoint);

        if (string.IsNullOrEmpty(config.ModelName))
        {
            throw new InvalidOperationException("Model name is required for Ollama provider.");
        }

        // Create the base Ollama client
        var client = new OllamaApiClient(endpoint, config.ModelName);

        // Apply client factory middleware if provided
        IChatClient chatClient = client;
        return chatClient;
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new OllamaErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://ollama.com/"),
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
    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name is required for Ollama");

        if (!string.IsNullOrEmpty(config.Endpoint) && !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
            errors.Add("Endpoint must be a valid, absolute URI");

        // Validate Ollama-specific config if present
        var ollamaConfig = config.GetProviderConfig<OllamaProviderConfig>();
        if (ollamaConfig != null)
        {
            // Validate Temperature range
            if (ollamaConfig.Temperature.HasValue && (ollamaConfig.Temperature.Value < 0 || ollamaConfig.Temperature.Value > 2))
            {
                errors.Add("Temperature must be between 0 and 2");
            }

            // Validate TopP range
            if (ollamaConfig.TopP.HasValue && (ollamaConfig.TopP.Value < 0 || ollamaConfig.TopP.Value > 1))
            {
                errors.Add("TopP must be between 0 and 1");
            }

            // Validate MinP range
            if (ollamaConfig.MinP.HasValue && (ollamaConfig.MinP.Value < 0 || ollamaConfig.MinP.Value > 1))
            {
                errors.Add("MinP must be between 0 and 1");
            }

            // Validate TypicalP range
            if (ollamaConfig.TypicalP.HasValue && (ollamaConfig.TypicalP.Value < 0 || ollamaConfig.TypicalP.Value > 1))
            {
                errors.Add("TypicalP must be between 0 and 1");
            }

            // Validate TfsZ
            if (ollamaConfig.TfsZ.HasValue && ollamaConfig.TfsZ.Value < 0)
            {
                errors.Add("TfsZ must be greater than or equal to 0");
            }

            // Validate RepeatPenalty
            if (ollamaConfig.RepeatPenalty.HasValue && ollamaConfig.RepeatPenalty.Value < 0)
            {
                errors.Add("RepeatPenalty must be greater than or equal to 0");
            }

            // Validate PresencePenalty range
            if (ollamaConfig.PresencePenalty.HasValue && (ollamaConfig.PresencePenalty.Value < 0 || ollamaConfig.PresencePenalty.Value > 2))
            {
                errors.Add("PresencePenalty must be between 0 and 2");
            }

            // Validate FrequencyPenalty range
            if (ollamaConfig.FrequencyPenalty.HasValue && (ollamaConfig.FrequencyPenalty.Value < 0 || ollamaConfig.FrequencyPenalty.Value > 2))
            {
                errors.Add("FrequencyPenalty must be between 0 and 2");
            }

            // Validate MiroStat
            if (ollamaConfig.MiroStat.HasValue && (ollamaConfig.MiroStat.Value < 0 || ollamaConfig.MiroStat.Value > 2))
            {
                errors.Add("MiroStat must be 0 (disabled), 1 (Mirostat), or 2 (Mirostat 2.0)");
            }

            // Validate MiroStatEta
            if (ollamaConfig.MiroStatEta.HasValue && ollamaConfig.MiroStatEta.Value < 0)
            {
                errors.Add("MiroStatEta must be greater than or equal to 0");
            }

            // Validate MiroStatTau
            if (ollamaConfig.MiroStatTau.HasValue && ollamaConfig.MiroStatTau.Value < 0)
            {
                errors.Add("MiroStatTau must be greater than or equal to 0");
            }

            // Validate NumPredict
            if (ollamaConfig.NumPredict.HasValue && ollamaConfig.NumPredict.Value < -2)
            {
                errors.Add("NumPredict must be greater than or equal to -2 (-2 = fill context, -1 = infinite, 0+ = specific count)");
            }

            // Validate NumCtx
            if (ollamaConfig.NumCtx.HasValue && ollamaConfig.NumCtx.Value < 1)
            {
                errors.Add("NumCtx must be greater than 0");
            }

            // Validate TopK
            if (ollamaConfig.TopK.HasValue && ollamaConfig.TopK.Value < 1)
            {
                errors.Add("TopK must be greater than 0");
            }
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }
}
