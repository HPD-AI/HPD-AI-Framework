#pragma warning disable MEAI001

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Meai = Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Replicate;

/// <summary>
/// Replicate provider implementation scoped to HPD image generation.
/// </summary>
internal sealed class ReplicateProvider : IImageGeneratorProvider
{
    internal const string DefaultModel = "black-forest-labs/flux-schnell";
    private static readonly Uri DefaultProviderUri = new("https://replicate.com/");
    private const string DefaultPrefer = "wait=60";
    private const string DefaultOutputMediaType = "image/webp";

    public string ProviderKey => "replicate";
    public string DisplayName => "Replicate";

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in ReplicateProviderModule.")]
    public Meai.IImageGenerator CreateImageGenerator(ClientProviderConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var modelName = string.IsNullOrWhiteSpace(config.ModelName)
            ? DefaultModel
            : config.ModelName;
        var replicateConfig = config.GetProviderConfig<ReplicateProviderConfig>(ProviderClientFamily.ImageGeneration)
            ?? config.GetProviderConfig<ReplicateProviderConfig>();
        var model = ParseModel(modelName!, replicateConfig?.ModelOwner);
        var client = CreateReplicateClient(config, services);

        return new ReplicateImageGenerator(client, model.Owner, model.Name, replicateConfig);
    }

    public IProviderErrorHandler CreateErrorHandler() => new ReplicateErrorHandler();

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://replicate.com/docs"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.ImageGeneration] = new()
                {
                    Family = ProviderClientFamily.ImageGeneration,
                    DefaultModelId = DefaultModel,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = false,
                        ["UsesModelPredictions"] = true
                    }
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider registers an AOT-compatible config deserializer in ReplicateProviderModule.")]
    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family != ProviderClientFamily.ImageGeneration)
            errors.Add("Replicate currently supports only image generation in HPD Agent.");

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            errors.Add("API key is required for Replicate. " +
                       "Set it via the apiKey parameter, REPLICATE_API_KEY or REPLICATE_API_TOKEN environment variable, or configuration.");
        }

        var replicateConfig = config.GetProviderConfig<ReplicateProviderConfig>(ProviderClientFamily.ImageGeneration)
            ?? config.GetProviderConfig<ReplicateProviderConfig>();
        var modelName = string.IsNullOrWhiteSpace(config.ModelName) ? DefaultModel : config.ModelName;
        ValidateModel(modelName!, replicateConfig?.ModelOwner, errors);

        if (!string.IsNullOrWhiteSpace(config.Endpoint) &&
            !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
        {
            errors.Add("Endpoint must be a valid, absolute URI");
        }

        if (replicateConfig is not null)
            ValidateProviderOptions(replicateConfig, errors);

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    internal static void ValidateProviderOptions(ReplicateProviderConfig config, List<string> errors)
    {
        if (config.ModelOwner is { Length: 0 })
            errors.Add("ModelOwner cannot be empty");

        if (config.Prefer is { Length: 0 })
            errors.Add("Prefer cannot be empty");

        if (config.TimeoutSeconds.HasValue && config.TimeoutSeconds.Value <= 0)
            errors.Add("TimeoutSeconds must be greater than 0");

        if (config.PollingIntervalSeconds.HasValue && config.PollingIntervalSeconds.Value <= 0)
            errors.Add("PollingIntervalSeconds must be greater than 0");

        if (config.OutputMediaType is { Length: 0 })
            errors.Add("OutputMediaType cannot be empty");

        if (config.Input is not null)
        {
            foreach (var key in config.Input.Keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    errors.Add("Input keys cannot be empty");
            }
        }
    }

    private static global::Replicate.ReplicateClient CreateReplicateClient(ClientProviderConfig config, IServiceProvider? services)
    {
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets is null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        var apiKeyTask = secrets.RequireAsync("replicate:ApiKey", "Replicate", config.ApiKey, CancellationToken.None);
        var apiKey = apiKeyTask.GetAwaiter().GetResult();
        var endpoint = string.IsNullOrWhiteSpace(config.Endpoint)
            ? new Uri("https://api.replicate.com/v1/")
            : new Uri(config.Endpoint, UriKind.Absolute);

        var client = new global::Replicate.ReplicateClient(baseUri: endpoint);
        client.AuthorizeUsingBearer(apiKey);
        return client;
    }

    private static void ValidateModel(string modelName, string? modelOwner, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            errors.Add("Model name is required for Replicate image generation");
            return;
        }

        if (!string.IsNullOrWhiteSpace(modelOwner))
            return;

        var slash = modelName.IndexOf('/');
        if (slash <= 0 || slash == modelName.Length - 1)
            errors.Add("Replicate ModelName must use owner/model format unless ModelOwner is configured.");
    }

    private static ReplicateModel ParseModel(string modelName, string? modelOwner)
    {
        if (!string.IsNullOrWhiteSpace(modelOwner))
            return new ReplicateModel(modelOwner, modelName);

        var slash = modelName.IndexOf('/');
        if (slash <= 0 || slash == modelName.Length - 1)
            throw new InvalidOperationException("Replicate ModelName must use owner/model format unless ModelOwner is configured.");

        return new ReplicateModel(modelName[..slash], modelName[(slash + 1)..]);
    }

    private readonly record struct ReplicateModel(string Owner, string Name);

    private sealed class ReplicateImageGenerator : Meai.IImageGenerator
    {
        private readonly global::Replicate.ReplicateClient _client;
        private readonly string _modelOwner;
        private readonly string _modelName;
        private readonly ReplicateProviderConfig? _config;
        private Meai.ImageGeneratorMetadata? _metadata;

        public ReplicateImageGenerator(
            global::Replicate.ReplicateClient client,
            string modelOwner,
            string modelName,
            ReplicateProviderConfig? config)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _modelOwner = modelOwner;
            _modelName = modelName;
            _config = config;
        }

        public Meai.ImageGeneratorMetadata Metadata =>
            _metadata ??= new Meai.ImageGeneratorMetadata("replicate", DefaultProviderUri, $"{_modelOwner}/{_modelName}");

        public void Dispose() => _client.Dispose();

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(Meai.ImageGeneratorMetadata))
                return Metadata;

            if (serviceType == typeof(global::Replicate.ReplicateClient))
                return _client;

            return null;
        }

        public async Task<Meai.ImageGenerationResponse> GenerateAsync(
            Meai.ImageGenerationRequest request,
            Meai.ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.Prompt))
                throw new ArgumentException("Prompt is required for Replicate image generation.", nameof(request));

            var input = CreateInput(request, options, _config);
            var prefer = GetAdditionalString(options, "replicate:prefer") ?? _config?.Prefer ?? DefaultPrefer;
            var response = await _client.ModelsPredictionsCreateAsync(
                modelOwner: _modelOwner,
                modelName: _modelName,
                input: input,
                prefer: prefer,
                webhook: null,
                stream: false,
                webhookEventsFilter: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.Status is not global::Replicate.SchemasPredictionResponseStatus.Succeeded)
            {
                response = await WaitUntilSuccessfulAsync(
                    response,
                    TimeSpan.FromSeconds(_config?.PollingIntervalSeconds ?? 2),
                    TimeSpan.FromSeconds(_config?.TimeoutSeconds ?? 120),
                    cancellationToken).ConfigureAwait(false);
            }

            var result = new Meai.ImageGenerationResponse(CreateContents(response.Output, options, _config))
            {
                RawRepresentation = response
            };
            return result;
        }

        private static Dictionary<string, object?> CreateInput(
            Meai.ImageGenerationRequest request,
            Meai.ImageGenerationOptions? options,
            ReplicateProviderConfig? config)
        {
            var input = new Dictionary<string, object?>(StringComparer.Ordinal);
            Merge(input, (IEnumerable<KeyValuePair<string, object?>>?)config?.Input);

            if (options?.AdditionalProperties is { } additionalProperties &&
                additionalProperties.TryGetValue("replicate:input", out var extraInput))
            {
                Merge(input, extraInput as IEnumerable<KeyValuePair<string, object?>>);
                if (extraInput is IReadOnlyDictionary<string, object?> readOnly)
                    Merge(input, readOnly);
                else if (extraInput is IDictionary dictionary)
                    Merge(input, dictionary);
            }

            input["prompt"] = request.Prompt;

            if (options?.Count is { } count)
                input["num_outputs"] = count;

            if (options?.ImageSize is { } imageSize)
                AddImageSize(input, imageSize);

            return input;
        }

        private static void AddImageSize(Dictionary<string, object?> input, Size imageSize)
        {
            if (imageSize.Width > 0)
                input.TryAdd("width", imageSize.Width);

            if (imageSize.Height > 0)
                input.TryAdd("height", imageSize.Height);
        }

        private static void Merge(Dictionary<string, object?> target, IEnumerable<KeyValuePair<string, object?>>? source)
        {
            if (source is null)
                return;

            foreach (var item in source)
            {
                if (!string.IsNullOrWhiteSpace(item.Key))
                    target[item.Key] = item.Value;
            }
        }

        private static void Merge(Dictionary<string, object?> target, IReadOnlyDictionary<string, object?> source)
        {
            foreach (var item in source)
            {
                if (!string.IsNullOrWhiteSpace(item.Key))
                    target[item.Key] = item.Value;
            }
        }

        private static void Merge(Dictionary<string, object?> target, IDictionary source)
        {
            foreach (DictionaryEntry entry in source)
            {
                if (entry.Key is string key && !string.IsNullOrWhiteSpace(key))
                    target[key] = entry.Value;
            }
        }

        private static IList<Meai.AIContent> CreateContents(
            object? output,
            Meai.ImageGenerationOptions? options,
            ReplicateProviderConfig? config)
        {
            var mediaType = options?.MediaType ?? config?.OutputMediaType ?? DefaultOutputMediaType;
            var contents = new List<Meai.AIContent>();
            AddOutput(contents, output, mediaType);
            return contents;
        }

        private static void AddOutput(List<Meai.AIContent> contents, object? output, string mediaType)
        {
            switch (output)
            {
                case null:
                    return;
                case string value:
                    AddStringOutput(contents, value, mediaType);
                    return;
                case JsonElement json:
                    AddJsonOutput(contents, json, mediaType);
                    return;
                case IEnumerable enumerable when output is not string:
                    foreach (var item in enumerable)
                        AddOutput(contents, item, mediaType);
                    return;
                default:
                    contents.Add(new Meai.TextContent(output.ToString() ?? string.Empty));
                    return;
            }
        }

        private static void AddJsonOutput(List<Meai.AIContent> contents, JsonElement json, string mediaType)
        {
            switch (json.ValueKind)
            {
                case JsonValueKind.String:
                    AddStringOutput(contents, json.GetString() ?? string.Empty, mediaType);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in json.EnumerateArray())
                        AddJsonOutput(contents, item, mediaType);
                    break;
                default:
                    contents.Add(new Meai.TextContent(json.GetRawText()));
                    break;
            }
        }

        private static void AddStringOutput(List<Meai.AIContent> contents, string value, string mediaType)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
                contents.Add(new Meai.UriContent(uri, mediaType));
            else
                contents.Add(new Meai.TextContent(value));
        }

        private static string? GetAdditionalString(Meai.ImageGenerationOptions? options, string key)
        {
            if (options?.AdditionalProperties is null ||
                !options.AdditionalProperties.TryGetValue(key, out var value))
            {
                return null;
            }

            return value as string;
        }

        private async Task<global::Replicate.SchemasPredictionResponse> WaitUntilSuccessfulAsync(
            global::Replicate.SchemasPredictionResponse response,
            TimeSpan pollingInterval,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var startedAt = DateTimeOffset.UtcNow;

            while (!IsCompleted(response))
            {
                if (DateTimeOffset.UtcNow - startedAt > timeout)
                    throw new TimeoutException($"Replicate prediction {response.Id} did not complete within {timeout}.");

                await Task.Delay(pollingInterval, cancellationToken).ConfigureAwait(false);
                response = await _client.PredictionsGetAsync(response.Id, cancellationToken).ConfigureAwait(false);
            }

            if (response.Status is global::Replicate.SchemasPredictionResponseStatus.Succeeded)
                return response;

            throw new InvalidOperationException($"Replicate prediction {response.Id} ended with status {response.Status}: {response.Error}");
        }

        private static bool IsCompleted(global::Replicate.SchemasPredictionResponse response) =>
            response.Status is
                global::Replicate.SchemasPredictionResponseStatus.Succeeded or
                global::Replicate.SchemasPredictionResponseStatus.Failed or
                global::Replicate.SchemasPredictionResponseStatus.Canceled;
    }
}
