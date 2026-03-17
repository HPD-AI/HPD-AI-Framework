using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HuggingFace;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.HuggingFace;

/// <summary>
/// HuggingFace Serverless Inference API provider implementation.
/// Supports text generation models hosted on HuggingFace's Inference API.
/// </summary>
/// <remarks>
/// <para>
/// This provider uses the HuggingFace Serverless Inference API which provides:
/// - Free access to thousands of models
/// - Automatic model loading and caching
/// - Rate limiting based on your account tier
/// - Support for various model architectures (LLMs, code models, etc.)
/// </para>
/// <para>
/// Supported model types:
/// - Text generation models (GPT, LLaMA, Mistral, etc.)
/// - Instruction-tuned models (chat/instruct variants)
/// - Code generation models (StarCoder, CodeLLaMA, etc.)
/// </para>
/// <para>
/// Authentication:
/// - Requires a HuggingFace API token (HF_TOKEN)
/// - Get your token from: https://huggingface.co/settings/tokens
/// </para>
/// </remarks>
internal class HuggingFaceProvider : IProviderFeatures
{
    public string ProviderKey => "huggingface";
    public string DisplayName => "Hugging Face";

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public IChatClient CreateChatClient(ProviderConfig config, IServiceProvider? services = null)
    {
        // Get secret resolver from services
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets == null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        // Resolve API key using ISecretResolver
        var apiKeyTask = secrets.RequireAsync("huggingface:ApiKey", "Hugging Face", config.ApiKey, CancellationToken.None);
        string apiKey = apiKeyTask.GetAwaiter().GetResult();

        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For HuggingFace, the ModelName (repository ID) must be configured.");
        }

        // Create the HuggingFace client
        var client = new HuggingFaceClient(apiKey);

        // Get typed config for advanced options
        var hfConfig = config.GetTypedProviderConfig<HuggingFaceProviderConfig>();

        // Wrap the client to apply configuration options
        return new HuggingFaceConfiguredChatClient(client, modelName, hfConfig);
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new HuggingFaceErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            SupportsStreaming = true,
            SupportsFunctionCalling = false, // Not supported by HF Serverless Inference API
            SupportsVision = false, // Not supported in current implementation
            DocumentationUrl = "https://huggingface.co/docs/api-inference/index"
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public ProviderValidationResult ValidateConfiguration(ProviderConfig config)
    {
        var errors = new List<string>();

        // Note: API key validation is now deferred to CreateChatClient where ISecretResolver is available
        // This method only validates config structure, not secret resolution
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            errors.Add("API key is required for Hugging Face. " +
                      "Set it via the apiKey parameter, HF_TOKEN or HUGGINGFACE_API_KEY environment variable, or configuration.");
        }

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name (repository ID like 'meta-llama/Meta-Llama-3-8B-Instruct') is required");

        // Validate HuggingFace-specific config if present
        var hfConfig = config.GetTypedProviderConfig<HuggingFaceProviderConfig>();
        if (hfConfig != null)
        {
            // Validate Temperature range
            if (hfConfig.Temperature.HasValue && (hfConfig.Temperature.Value < 0 || hfConfig.Temperature.Value > 100))
            {
                errors.Add("Temperature must be between 0 and 100");
            }

            // Validate TopP range
            if (hfConfig.TopP.HasValue && (hfConfig.TopP.Value < 0 || hfConfig.TopP.Value > 1))
            {
                errors.Add("TopP must be between 0 and 1");
            }

            // Validate TopK
            if (hfConfig.TopK.HasValue && hfConfig.TopK.Value < 0)
            {
                errors.Add("TopK must be a positive integer");
            }

            // Validate RepetitionPenalty
            if (hfConfig.RepetitionPenalty.HasValue && hfConfig.RepetitionPenalty.Value < 0)
            {
                errors.Add("RepetitionPenalty must be a positive number");
            }

            // Validate MaxNewTokens
            if (hfConfig.MaxNewTokens.HasValue && hfConfig.MaxNewTokens.Value < 1)
            {
                errors.Add("MaxNewTokens must be at least 1");
            }

            // Validate NumReturnSequences
            if (hfConfig.NumReturnSequences.HasValue && hfConfig.NumReturnSequences.Value < 1)
            {
                errors.Add("NumReturnSequences must be at least 1");
            }

            // Validate MaxTime
            if (hfConfig.MaxTime.HasValue && hfConfig.MaxTime.Value <= 0)
            {
                errors.Add("MaxTime must be a positive number (seconds)");
            }
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    /// <summary>
    /// Wrapper chat client that applies HuggingFace configuration options to requests.
    /// </summary>
    private class HuggingFaceConfiguredChatClient : IChatClient
    {
        private readonly HuggingFaceClient _client;
        private readonly string _modelName;
        private readonly HuggingFaceProviderConfig? _config;
        private ChatClientMetadata? _metadata;

        public HuggingFaceConfiguredChatClient(
            HuggingFaceClient client,
            string modelName,
            HuggingFaceProviderConfig? config)
        {
            _client = client;
            _modelName = modelName;
            _config = config;
        }

        public void Dispose()
        {
            // Dispose the underlying client
            _client?.Dispose();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return
                serviceKey is not null ? null :
                serviceType == typeof(ChatClientMetadata) ? (_metadata ??= new(nameof(HuggingFaceClient), _client.BaseUri, _modelName)) :
                serviceType?.IsInstanceOfType(_client) is true ? _client :
                null;
        }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            // Set model name in options
            options ??= new ChatOptions();
            options.ModelId = _modelName;

            // Apply configuration through RawRepresentationFactory
            if (_config != null)
            {
                options.RawRepresentationFactory = _ => CreateConfiguredRequest();
            }

            return await ((IChatClient)_client).GetResponseAsync(messages, options, cancellationToken);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            // Set model name in options
            options ??= new ChatOptions();
            options.ModelId = _modelName;

            // Apply configuration through RawRepresentationFactory
            if (_config != null)
            {
                options.RawRepresentationFactory = _ => CreateConfiguredRequest();
            }

            return ((IChatClient)_client).GetStreamingResponseAsync(messages, options, cancellationToken);
        }

        private GenerateTextRequest CreateConfiguredRequest()
        {
            var request = new GenerateTextRequest { Inputs = string.Empty };
            var parameters = new GenerateTextRequestParameters();
            var requestOptions = new GenerateTextRequestOptions();

            // Apply configuration
            if (_config != null)
            {
                // Sampling parameters
                if (_config.Temperature.HasValue)
                    parameters.Temperature = _config.Temperature.Value;

                if (_config.TopP.HasValue)
                    parameters.TopP = _config.TopP.Value;

                if (_config.TopK.HasValue)
                    parameters.TopK = _config.TopK.Value;

                if (_config.RepetitionPenalty.HasValue)
                    parameters.RepetitionPenalty = _config.RepetitionPenalty.Value;

                // Generation control
                if (_config.MaxNewTokens.HasValue)
                    parameters.MaxNewTokens = _config.MaxNewTokens.Value;

                if (_config.DoSample.HasValue)
                    parameters.DoSample = _config.DoSample.Value;

                if (_config.NumReturnSequences.HasValue)
                    parameters.NumReturnSequences = _config.NumReturnSequences.Value;

                if (_config.ReturnFullText.HasValue)
                    parameters.ReturnFullText = _config.ReturnFullText.Value;

                // Timing
                if (_config.MaxTime.HasValue)
                    parameters.MaxTime = _config.MaxTime.Value;

                // API options
                if (_config.UseCache.HasValue)
                    requestOptions.UseCache = _config.UseCache.Value;

                if (_config.WaitForModel.HasValue)
                    requestOptions.WaitForModel = _config.WaitForModel.Value;
            }

            request.Parameters = parameters;
            request.Options = requestOptions;

            return request;
        }
    }
}
