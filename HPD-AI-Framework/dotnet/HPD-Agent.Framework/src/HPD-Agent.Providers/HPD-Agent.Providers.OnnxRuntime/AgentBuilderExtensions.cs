using System;
using System.Collections.Generic;
using System.IO;
using HPD.Agent;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.OnnxRuntime;

/// <summary>
/// Extension methods for AgentBuilder to configure ONNX Runtime as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use ONNX Runtime GenAI as the AI provider for local model inference.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="modelPath">The path to the ONNX model directory containing the model files</param>
    /// <param name="configure">Optional action to configure additional ONNX Runtime-specific options</param>
    /// <param name="clientFactory">Optional factory to wrap the chat client with middleware (logging, caching, etc.)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// Model Path Resolution (in priority order):
    /// 1. Explicit modelPath parameter
    /// 2. Environment variable: ONNX_MODEL_PATH
    /// </para>
    /// <para>
    /// This method creates an <see cref="OnnxRuntimeProviderConfig"/> that is:
    /// - Stored in <c>ProviderConfig.ProviderOptionsJson</c> for FFI/JSON serialization
    /// - Applied during <c>OnnxRuntimeProvider.CreateChatClient()</c> via the registered deserializer
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same config structure directly:
    /// <code>
    /// {
    ///   "Provider": {
    ///     "ProviderKey": "onnx-runtime",
    ///     "ModelName": "phi-3-mini",
    ///     "ProviderOptionsJson": "{\"modelPath\":\"/path/to/model\",\"maxLength\":2048,\"temperature\":0.7,\"doSample\":true}"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: Basic configuration
    /// var agent = await new AgentBuilder()
    ///     .WithOnnxRuntime(
    ///         modelPath: "/path/to/phi-3-mini",
    ///         configure: opts =>
    ///         {
    ///             opts.MaxLength = 2048;
    ///             opts.Temperature = 0.7f;
    ///             opts.DoSample = true;
    ///         })
    ///     .Build();
    ///
    /// // Option 2: Beam search configuration
    /// var agent = await new AgentBuilder()
    ///     .WithOnnxRuntime(
    ///         modelPath: "/path/to/llama-2-7b",
    ///         configure: opts =>
    ///         {
    ///             opts.MaxLength = 1024;
    ///             opts.NumBeams = 4;
    ///             opts.EarlyStopping = true;
    ///             opts.LengthPenalty = 1.2f;
    ///         })
    ///     .Build();
    ///
    /// // Option 3: CUDA with custom providers
    /// var agent = await new AgentBuilder()
    ///     .WithOnnxRuntime(
    ///         modelPath: "/path/to/model",
    ///         configure: opts =>
    ///         {
    ///             opts.Providers = new List&lt;string&gt; { "cuda", "cpu" };
    ///             opts.ProviderOptions = new Dictionary&lt;string, Dictionary&lt;string, string&gt;&gt;
    ///             {
    ///                 ["cuda"] = new Dictionary&lt;string, string&gt;
    ///                 {
    ///                     ["device_id"] = "0",
    ///                     ["cudnn_conv_algo_search"] = "DEFAULT"
    ///                 }
    ///             };
    ///         })
    ///     .Build();
    ///
    /// // Option 4: Constrained generation with JSON schema
    /// var agent = await new AgentBuilder()
    ///     .WithOnnxRuntime(
    ///         modelPath: "/path/to/model",
    ///         configure: opts =>
    ///         {
    ///             opts.GuidanceType = "json";
    ///             opts.GuidanceData = @"{
    ///                 ""type"": ""object"",
    ///                 ""properties"": {
    ///                     ""name"": { ""type"": ""string"" },
    ///                     ""age"": { ""type"": ""number"" }
    ///                 }
    ///             }";
    ///         })
    ///     .Build();
    ///
    /// // Option 5: Sampling with top-k and top-p
    /// var agent = await new AgentBuilder()
    ///     .WithOnnxRuntime(
    ///         modelPath: "/path/to/model",
    ///         configure: opts =>
    ///         {
    ///             opts.DoSample = true;
    ///             opts.TopK = 50;
    ///             opts.TopP = 0.9f;
    ///             opts.Temperature = 0.8f;
    ///             opts.RepetitionPenalty = 1.1f;
    ///             opts.RandomSeed = 42; // For deterministic sampling
    ///         })
    ///     .Build();
    ///
    /// // Option 6: Multi-LoRA adapter support
    /// var agent = await new AgentBuilder()
    ///     .WithOnnxRuntime(
    ///         modelPath: "/path/to/base-model",
    ///         configure: opts =>
    ///         {
    ///             opts.AdapterPath = "/path/to/adapters.onnx_adapter";
    ///             opts.AdapterName = "math_adapter";
    ///         })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithOnnxRuntime(
        this AgentBuilder builder,
        string modelPath,
        Action<OnnxRuntimeProviderConfig>? configure = null,
        Func<IChatClient, IChatClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("Model path is required for ONNX Runtime provider.", nameof(modelPath));

        // Validate model path exists
        if (!Directory.Exists(modelPath))
            throw new DirectoryNotFoundException($"Model path does not exist: {modelPath}");

        // Create provider config
        var providerConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = modelPath
        };

        // Allow user to configure additional options
        configure?.Invoke(providerConfig);

        // Validate configuration
        ValidateProviderConfig(providerConfig, configure);

        // Build provider config
        builder.Config.Provider = new ProviderConfig
        {
            ProviderKey = "onnx-runtime",
            ModelName = Path.GetFileName(modelPath) // Use directory name as model name
        };

        // Store the typed config
        builder.Config.Provider.SetTypedProviderConfig(providerConfig);

        // Store the client factory if provided
        if (clientFactory != null)
        {
            builder.Config.Provider.AdditionalProperties ??= new Dictionary<string, object>();
            builder.Config.Provider.AdditionalProperties["ClientFactory"] = clientFactory;
        }

        return builder;
    }

    private static void ValidateProviderConfig(OnnxRuntimeProviderConfig config, Action<OnnxRuntimeProviderConfig>? configure)
    {
        // Validate model path
        if (string.IsNullOrWhiteSpace(config.ModelPath))
        {
            throw new ArgumentException(
                "ModelPath is required for ONNX Runtime provider.",
                nameof(configure));
        }

        // Validate MaxLength
        if (config.MaxLength.HasValue && config.MaxLength.Value < 1)
        {
            throw new ArgumentException(
                "MaxLength must be greater than 0.",
                nameof(configure));
        }

        // Validate MinLength
        if (config.MinLength.HasValue && config.MinLength.Value < 0)
        {
            throw new ArgumentException(
                "MinLength must be greater than or equal to 0.",
                nameof(configure));
        }

        // Validate MinLength <= MaxLength
        if (config.MinLength.HasValue && config.MaxLength.HasValue &&
            config.MinLength.Value > config.MaxLength.Value)
        {
            throw new ArgumentException(
                "MinLength cannot be greater than MaxLength.",
                nameof(configure));
        }

        // Validate BatchSize
        if (config.BatchSize.HasValue && config.BatchSize.Value < 1)
        {
            throw new ArgumentException(
                "BatchSize must be greater than 0.",
                nameof(configure));
        }

        // Validate Temperature
        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 2))
        {
            throw new ArgumentException(
                "Temperature must be between 0 and 2.",
                nameof(configure));
        }

        // Validate TopP
        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
        {
            throw new ArgumentException(
                "TopP must be between 0 and 1.",
                nameof(configure));
        }

        // Validate TopK
        if (config.TopK.HasValue && config.TopK.Value < 1)
        {
            throw new ArgumentException(
                "TopK must be greater than 0.",
                nameof(configure));
        }

        // Validate RepetitionPenalty
        if (config.RepetitionPenalty.HasValue && config.RepetitionPenalty.Value <= 0)
        {
            throw new ArgumentException(
                "RepetitionPenalty must be greater than 0.",
                nameof(configure));
        }

        // Validate NumBeams
        if (config.NumBeams.HasValue && config.NumBeams.Value < 1)
        {
            throw new ArgumentException(
                "NumBeams must be greater than 0.",
                nameof(configure));
        }

        // Validate NumReturnSequences
        if (config.NumReturnSequences.HasValue && config.NumReturnSequences.Value < 1)
        {
            throw new ArgumentException(
                "NumReturnSequences must be greater than 0.",
                nameof(configure));
        }

        // Validate NumReturnSequences <= NumBeams
        if (config.NumReturnSequences.HasValue && config.NumBeams.HasValue &&
            config.NumReturnSequences.Value > config.NumBeams.Value)
        {
            throw new ArgumentException(
                "NumReturnSequences cannot be greater than NumBeams.",
                nameof(configure));
        }

        // Validate LengthPenalty
        if (config.LengthPenalty.HasValue && config.LengthPenalty.Value <= 0)
        {
            throw new ArgumentException(
                "LengthPenalty must be greater than 0.",
                nameof(configure));
        }

        // Validate ChunkSize
        if (config.ChunkSize.HasValue && config.ChunkSize.Value < 1)
        {
            throw new ArgumentException(
                "ChunkSize must be greater than 0.",
                nameof(configure));
        }

        // Validate Guidance configuration
        if (!string.IsNullOrEmpty(config.GuidanceType) && string.IsNullOrEmpty(config.GuidanceData))
        {
            throw new ArgumentException(
                "GuidanceData is required when GuidanceType is specified.",
                nameof(configure));
        }

        if (!string.IsNullOrEmpty(config.GuidanceData) && string.IsNullOrEmpty(config.GuidanceType))
        {
            throw new ArgumentException(
                "GuidanceType is required when GuidanceData is specified.",
                nameof(configure));
        }

        // Validate Adapter configuration
        if (!string.IsNullOrEmpty(config.AdapterName) && string.IsNullOrEmpty(config.AdapterPath))
        {
            throw new ArgumentException(
                "AdapterPath is required when AdapterName is specified.",
                nameof(configure));
        }

        // Validate adapter path exists if specified
        if (!string.IsNullOrEmpty(config.AdapterPath) && !File.Exists(config.AdapterPath))
        {
            throw new FileNotFoundException(
                $"Adapter file not found: {config.AdapterPath}",
                config.AdapterPath);
        }

        // Validate sampling configuration
        if (config.DoSample == true)
        {
            // Warn if sampling is enabled but no sampling parameters are set
            if (!config.TopK.HasValue && !config.TopP.HasValue)
            {
                // This is just a note - not an error. ONNX Runtime will use defaults.
            }
        }
        else if (config.DoSample == false)
        {
            // Warn if sampling parameters are set but sampling is disabled
            if (config.TopK.HasValue || config.TopP.HasValue || config.Temperature.HasValue)
            {
                // This is just a note - sampling parameters will be ignored
            }
        }
    }
}
