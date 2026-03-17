using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntimeGenAI;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.OnnxRuntime;

/// <summary>
/// ONNX Runtime GenAI provider implementation for local model inference.
/// Supports CPU, CUDA, DirectML, QNN, OpenVINO, TensorRT, and WebGPU execution providers.
/// </summary>
/// <remarks>
/// <para>
/// This provider uses Microsoft's ONNX Runtime GenAI library for high-performance
/// local LLM inference with support for various hardware accelerators.
/// </para>
/// <para>
/// Supported Model Architectures:
/// - Llama, Mistral, Phi, Gemma, Qwen, DeepSeek, Granite, SmolLM3
/// - ChatGLM, ERNIE, Nemotron, AMD OLMo
/// - Whisper (audio), Phi Vision (multi-modal)
/// </para>
/// <para>
/// Hardware Acceleration:
/// - CPU (all platforms)
/// - CUDA (NVIDIA GPUs)
/// - DirectML (Windows with any GPU)
/// - QNN (Qualcomm NPUs)
/// - OpenVINO (Intel hardware)
/// - TensorRT (NVIDIA GPUs, optimized)
/// - WebGPU (browser-based inference)
/// </para>
/// </remarks>
internal class OnnxRuntimeProvider : IProviderFeatures
{
    public string ProviderKey => "onnx-runtime";
    public string DisplayName => "ONNX Runtime GenAI";

    public IChatClient CreateChatClient(ProviderConfig config, IServiceProvider? services = null)
    {
        // Get typed config
        var onnxConfig = config.GetTypedProviderConfig<OnnxRuntimeProviderConfig>();

        // Resolve model path
        string? modelPath = onnxConfig?.ModelPath ?? Environment.GetEnvironmentVariable("ONNX_MODEL_PATH");

        if (string.IsNullOrEmpty(modelPath))
        {
            throw new InvalidOperationException(
                "For the OnnxRuntime provider, the ModelPath must be configured. " +
                "Set it via WithOnnxRuntime(modelPath) or the ONNX_MODEL_PATH environment variable.");
        }

        if (!Directory.Exists(modelPath))
        {
            throw new DirectoryNotFoundException($"Model path does not exist: {modelPath}");
        }

        // Create Config object for advanced provider configuration
        Config? modelConfig = null;
        if (onnxConfig?.Providers != null || onnxConfig?.ProviderOptions != null)
        {
            modelConfig = new Config(modelPath);

            // Configure execution providers
            if (onnxConfig.Providers != null && onnxConfig.Providers.Count > 0)
            {
                modelConfig.ClearProviders();
                foreach (var provider in onnxConfig.Providers)
                {
                    modelConfig.AppendProvider(provider);
                }
            }

            // Configure provider options
            if (onnxConfig.ProviderOptions != null)
            {
                foreach (var providerKvp in onnxConfig.ProviderOptions)
                {
                    string providerName = providerKvp.Key;
                    foreach (var optionKvp in providerKvp.Value)
                    {
                        modelConfig.SetProviderOption(providerName, optionKvp.Key, optionKvp.Value);
                    }
                }
            }
        }

        // Create Model
        Model model = modelConfig != null ? new Model(modelConfig) : new Model(modelPath);

        // Load adapters if specified
        Adapters? adapters = null;
        if (!string.IsNullOrEmpty(onnxConfig?.AdapterPath))
        {
            adapters = new Adapters(model);
            string adapterName = onnxConfig.AdapterName ?? "default_adapter";
            adapters.LoadAdapter(onnxConfig.AdapterPath, adapterName);
        }

        // Configure OnnxRuntimeGenAIChatClient options
        var clientOptions = new OnnxRuntimeGenAIChatClientOptions
        {
            StopSequences = onnxConfig?.StopSequences,
            EnableCaching = onnxConfig?.EnableCaching ?? false,
            PromptFormatter = null // Will use default formatter unless overridden
        };

        // Get prompt formatter from AdditionalProperties if provided
        if (config.AdditionalProperties?.TryGetValue("PromptFormatter", out var formatterObj) == true &&
            formatterObj is Func<IEnumerable<ChatMessage>, ChatOptions?, string> formatter)
        {
            clientOptions.PromptFormatter = formatter;
        }

        // Create the chat client - OnnxRuntimeGenAIChatClient takes a string path, not a Model object
        var chatClient = new OnnxRuntimeGenAIChatClient(modelPath, clientOptions);

        // Store configuration for generator params (will be applied in the custom wrapper or via chat options)
        // Note: We create a wrapper that applies GeneratorParams settings
        var wrappedClient = new ConfigurableOnnxRuntimeChatClient(chatClient, onnxConfig);

        // Apply client factory middleware if provided
        IChatClient finalClient = wrappedClient;
        if (config.AdditionalProperties?.TryGetValue("ClientFactory", out var factoryObj) == true &&
            factoryObj is Func<IChatClient, IChatClient> clientFactory)
        {
            finalClient = clientFactory(finalClient);
        }

        return finalClient;
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new OnnxRuntimeErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            SupportsStreaming = true,
            SupportsFunctionCalling = false, // ONNX Runtime GenAI doesn't have built-in function calling yet
            SupportsVision = true, // Phi Vision and other multi-modal models are supported
            DocumentationUrl = "https://onnxruntime.ai/docs/genai/"
        };
    }

    public ProviderValidationResult ValidateConfiguration(ProviderConfig config)
    {
        var errors = new List<string>();

        // Get typed config
        var onnxConfig = config.GetTypedProviderConfig<OnnxRuntimeProviderConfig>();

        // Validate model path
        string? modelPath = onnxConfig?.ModelPath ?? Environment.GetEnvironmentVariable("ONNX_MODEL_PATH");

        if (string.IsNullOrEmpty(modelPath))
        {
            errors.Add("ModelPath is required. Configure it via WithOnnxRuntime(modelPath) or the ONNX_MODEL_PATH environment variable.");
        }
        else if (!Directory.Exists(modelPath))
        {
            errors.Add($"Model path does not exist: {modelPath}");
        }

        // Validate ONNX-specific config if present
        if (onnxConfig != null)
        {
            // Validate MaxLength
            if (onnxConfig.MaxLength.HasValue && onnxConfig.MaxLength.Value < 1)
            {
                errors.Add("MaxLength must be greater than 0");
            }

            // Validate MinLength
            if (onnxConfig.MinLength.HasValue && onnxConfig.MinLength.Value < 0)
            {
                errors.Add("MinLength must be greater than or equal to 0");
            }

            // Validate MinLength <= MaxLength
            if (onnxConfig.MinLength.HasValue && onnxConfig.MaxLength.HasValue &&
                onnxConfig.MinLength.Value > onnxConfig.MaxLength.Value)
            {
                errors.Add("MinLength cannot be greater than MaxLength");
            }

            // Validate Temperature range
            if (onnxConfig.Temperature.HasValue && (onnxConfig.Temperature.Value < 0 || onnxConfig.Temperature.Value > 2))
            {
                errors.Add("Temperature must be between 0 and 2");
            }

            // Validate TopP range
            if (onnxConfig.TopP.HasValue && (onnxConfig.TopP.Value < 0 || onnxConfig.TopP.Value > 1))
            {
                errors.Add("TopP must be between 0 and 1");
            }

            // Validate TopK
            if (onnxConfig.TopK.HasValue && onnxConfig.TopK.Value < 1)
            {
                errors.Add("TopK must be greater than 0");
            }

            // Validate RepetitionPenalty
            if (onnxConfig.RepetitionPenalty.HasValue && onnxConfig.RepetitionPenalty.Value <= 0)
            {
                errors.Add("RepetitionPenalty must be greater than 0");
            }

            // Validate NumBeams
            if (onnxConfig.NumBeams.HasValue && onnxConfig.NumBeams.Value < 1)
            {
                errors.Add("NumBeams must be greater than 0");
            }

            // Validate NumReturnSequences
            if (onnxConfig.NumReturnSequences.HasValue && onnxConfig.NumReturnSequences.Value < 1)
            {
                errors.Add("NumReturnSequences must be greater than 0");
            }

            // Validate NumReturnSequences <= NumBeams
            if (onnxConfig.NumReturnSequences.HasValue && onnxConfig.NumBeams.HasValue &&
                onnxConfig.NumReturnSequences.Value > onnxConfig.NumBeams.Value)
            {
                errors.Add("NumReturnSequences cannot be greater than NumBeams");
            }

            // Validate adapter path if specified
            if (!string.IsNullOrEmpty(onnxConfig.AdapterPath) && !File.Exists(onnxConfig.AdapterPath))
            {
                errors.Add($"Adapter file not found: {onnxConfig.AdapterPath}");
            }
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    /// <summary>
    /// Internal wrapper that applies GeneratorParams configuration from OnnxRuntimeProviderConfig.
    /// This allows us to apply all the search options (max_length, temperature, etc.) to each request.
    /// </summary>
    private class ConfigurableOnnxRuntimeChatClient : IChatClient
    {
        private readonly OnnxRuntimeGenAIChatClient _innerClient;
        private readonly OnnxRuntimeProviderConfig? _config;

        public ConfigurableOnnxRuntimeChatClient(
            OnnxRuntimeGenAIChatClient innerClient,
            OnnxRuntimeProviderConfig? config)
        {
            _innerClient = innerClient;
            _config = config;
        }

        public ChatClientMetadata Metadata { get; } = new ChatClientMetadata("onnx-runtime");

        public TService? GetService<TService>(object? key = null) where TService : class
            => _innerClient.GetService<TService>(key);

        public object? GetService(Type serviceType, object? key = null)
            => ((IChatClient)_innerClient).GetService(serviceType, key);

        public void Dispose() => _innerClient.Dispose();

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            // Merge our config with the options
            var mergedOptions = MergeOptions(options);
            return await _innerClient.GetResponseAsync(chatMessages, mergedOptions, cancellationToken);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            // Merge our config with the options
            var mergedOptions = MergeOptions(options);
            return _innerClient.GetStreamingResponseAsync(chatMessages, mergedOptions, cancellationToken);
        }

        private ChatOptions MergeOptions(ChatOptions? userOptions)
        {
            var options = userOptions ?? new ChatOptions();

            // Apply config values if not already set by user
            if (_config != null)
            {
                // MaxOutputTokens maps to max_length
                if (_config.MaxLength.HasValue && options.MaxOutputTokens == null)
                {
                    options.MaxOutputTokens = _config.MaxLength.Value;
                }

                // Temperature
                if (_config.Temperature.HasValue && options.Temperature == null)
                {
                    options.Temperature = _config.Temperature.Value;
                }

                // TopP
                if (_config.TopP.HasValue && options.TopP == null)
                {
                    options.TopP = _config.TopP.Value;
                }

                // TopK
                if (_config.TopK.HasValue && options.TopK == null)
                {
                    options.TopK = _config.TopK.Value;
                }

                // FrequencyPenalty maps to repetition_penalty
                if (_config.RepetitionPenalty.HasValue && options.FrequencyPenalty == null)
                {
                    options.FrequencyPenalty = _config.RepetitionPenalty.Value;
                }

                // StopSequences
                if (_config.StopSequences != null && options.StopSequences == null)
                {
                    options.StopSequences = _config.StopSequences;
                }

                // Seed for deterministic output
                if (_config.RandomSeed.HasValue && _config.RandomSeed.Value != -1 && options.Seed == null)
                {
                    options.Seed = (long)_config.RandomSeed.Value;
                }

                // Additional model-specific parameters that don't have direct ChatOptions mappings
                // Store these in AdditionalProperties for potential custom handling
                if (options.AdditionalProperties == null)
                {
                    options.AdditionalProperties = new Microsoft.Extensions.AI.AdditionalPropertiesDictionary();
                }

                if (_config.DoSample.HasValue)
                    options.AdditionalProperties["do_sample"] = _config.DoSample.Value;

                if (_config.MinLength.HasValue)
                    options.AdditionalProperties["min_length"] = _config.MinLength.Value;

                if (_config.NumBeams.HasValue)
                    options.AdditionalProperties["num_beams"] = _config.NumBeams.Value;

                if (_config.NumReturnSequences.HasValue)
                    options.AdditionalProperties["num_return_sequences"] = _config.NumReturnSequences.Value;

                if (_config.LengthPenalty.HasValue)
                    options.AdditionalProperties["length_penalty"] = _config.LengthPenalty.Value;

                if (_config.EarlyStopping.HasValue)
                    options.AdditionalProperties["early_stopping"] = _config.EarlyStopping.Value;

                if (_config.NoRepeatNgramSize.HasValue)
                    options.AdditionalProperties["no_repeat_ngram_size"] = _config.NoRepeatNgramSize.Value;

                if (_config.DiversityPenalty.HasValue)
                    options.AdditionalProperties["diversity_penalty"] = _config.DiversityPenalty.Value;

                if (_config.PastPresentShareBuffer.HasValue)
                    options.AdditionalProperties["past_present_share_buffer"] = _config.PastPresentShareBuffer.Value;

                if (_config.ChunkSize.HasValue)
                    options.AdditionalProperties["chunk_size"] = _config.ChunkSize.Value;

                // Guidance/constrained decoding
                if (!string.IsNullOrEmpty(_config.GuidanceType))
                {
                    options.AdditionalProperties["guidance_type"] = _config.GuidanceType;
                    options.AdditionalProperties["guidance_data"] = _config.GuidanceData;
                    options.AdditionalProperties["guidance_enable_ff_tokens"] = _config.GuidanceEnableFFTokens;
                }

                // Adapter configuration stored for reference
                if (!string.IsNullOrEmpty(_config.AdapterPath) && !string.IsNullOrEmpty(_config.AdapterName))
                {
                    options.AdditionalProperties["adapter_path"] = _config.AdapterPath;
                    options.AdditionalProperties["adapter_name"] = _config.AdapterName;
                }
            }

            return options;
        }
    }
}
