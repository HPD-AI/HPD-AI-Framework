using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
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
/// The provider maps OllamaProviderConfig to OllamaSharp's RequestOptions for
/// complete control over model behavior.
/// </para>
/// </remarks>
internal class OllamaProvider : IProviderFeatures
{
    public string ProviderKey => "ollama";
    public string DisplayName => "Ollama";

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public IChatClient CreateChatClient(ProviderConfig config, IServiceProvider? services = null)
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

        // Get typed config if available
        var ollamaConfig = config.GetTypedProviderConfig<OllamaProviderConfig>();

        // Apply configuration options if provided
        if (ollamaConfig != null)
        {
            // Note: OllamaSharp's OllamaApiClient applies RequestOptions per-request,
            // not at the client level. The options will be applied through the
            // Microsoft.Extensions.AI integration layer or by wrapping the client.
            // For now, we store the config in AdditionalProperties for potential
            // middleware or wrapper usage.
            config.AdditionalProperties ??= new Dictionary<string, object>();
            config.AdditionalProperties["OllamaRequestOptions"] = CreateRequestOptions(ollamaConfig);
        }

        // Apply client factory middleware if provided
        IChatClient chatClient = client;
        if (config.AdditionalProperties?.TryGetValue("ClientFactory", out var factoryObj) == true &&
            factoryObj is Func<IChatClient, IChatClient> clientFactory)
        {
            chatClient = clientFactory(chatClient);
        }

        return chatClient;
    }

    /// <summary>
    /// Creates OllamaSharp RequestOptions from OllamaProviderConfig.
    /// </summary>
    private static RequestOptions CreateRequestOptions(OllamaProviderConfig config)
    {
        var options = new RequestOptions();

        // Core parameters
        if (config.NumPredict.HasValue) options.NumPredict = config.NumPredict.Value;
        if (config.NumCtx.HasValue) options.NumCtx = config.NumCtx.Value;

        // Sampling parameters
        if (config.Temperature.HasValue) options.Temperature = config.Temperature.Value;
        if (config.TopP.HasValue) options.TopP = config.TopP.Value;
        if (config.TopK.HasValue) options.TopK = config.TopK.Value;
        if (config.MinP.HasValue) options.MinP = config.MinP.Value;
        if (config.TypicalP.HasValue) options.TypicalP = config.TypicalP.Value;
        if (config.TfsZ.HasValue) options.TfsZ = config.TfsZ.Value;

        // Repetition control
        if (config.RepeatPenalty.HasValue) options.RepeatPenalty = config.RepeatPenalty.Value;
        if (config.RepeatLastN.HasValue) options.RepeatLastN = config.RepeatLastN.Value;
        if (config.PresencePenalty.HasValue) options.PresencePenalty = config.PresencePenalty.Value;
        if (config.FrequencyPenalty.HasValue) options.FrequencyPenalty = config.FrequencyPenalty.Value;
        if (config.PenalizeNewline.HasValue) options.PenalizeNewline = config.PenalizeNewline.Value;

        // Determinism
        if (config.Seed.HasValue) options.Seed = config.Seed.Value;
        if (config.Stop != null) options.Stop = config.Stop;

        // Mirostat sampling
        if (config.MiroStat.HasValue) options.MiroStat = config.MiroStat.Value;
        if (config.MiroStatEta.HasValue) options.MiroStatEta = config.MiroStatEta.Value;
        if (config.MiroStatTau.HasValue) options.MiroStatTau = config.MiroStatTau.Value;

        // Context and memory
        if (config.NumKeep.HasValue) options.NumKeep = config.NumKeep.Value;

        // Performance and hardware
        if (config.NumGpu.HasValue) options.NumGpu = config.NumGpu.Value;
        if (config.MainGpu.HasValue) options.MainGpu = config.MainGpu.Value;
        if (config.LowVram.HasValue) options.LowVram = config.LowVram.Value;
        if (config.F16kv.HasValue) options.F16kv = config.F16kv.Value;
        if (config.LogitsAll.HasValue) options.LogitsAll = config.LogitsAll.Value;

        // Threading and batch processing
        if (config.NumThread.HasValue) options.NumThread = config.NumThread.Value;
        if (config.NumBatch.HasValue) options.NumBatch = config.NumBatch.Value;
        if (config.NumGqa.HasValue) options.NumGqa = config.NumGqa.Value;

        // Memory management
        if (config.UseMmap.HasValue) options.UseMmap = config.UseMmap.Value;
        if (config.UseMlock.HasValue) options.UseMlock = config.UseMlock.Value;
        if (config.Numa.HasValue) options.Numa = config.Numa.Value;
        if (config.VocabOnly.HasValue) options.VocabOnly = config.VocabOnly.Value;

        return options;
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
            SupportsStreaming = true,
            SupportsFunctionCalling = true, // Ollama supports function calling for compatible models
            SupportsVision = true, // Ollama supports vision models
            DocumentationUrl = "https://ollama.com/"
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public ProviderValidationResult ValidateConfiguration(ProviderConfig config)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name is required for Ollama");

        if (!string.IsNullOrEmpty(config.Endpoint) && !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
            errors.Add("Endpoint must be a valid, absolute URI");

        // Validate Ollama-specific config if present
        var ollamaConfig = config.GetTypedProviderConfig<OllamaProviderConfig>();
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
