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
[HpdProvider("replicate", "Replicate")]
[HpdProviderFamily(ProviderClientFamily.ImageGeneration)]
[HpdProviderPayload(ProviderClientFamily.ImageGeneration, ProviderPayloadKind.Configuration, typeof(ReplicateProviderConfig), typeof(ReplicateJsonContext))]
[HpdProviderPayload(ProviderClientFamily.ImageGeneration, ProviderPayloadKind.OperationOptions, typeof(ReplicateImageOptions), typeof(ReplicateJsonContext))]
[HpdProviderSecretAlias("replicate:ApiKey", "REPLICATE_API_KEY", "REPLICATE_API_TOKEN")]
internal sealed class ReplicateProvider : IImageGeneratorProvider, IProviderSecretAliasProvider
{
    internal const string DefaultModel = "black-forest-labs/flux-schnell";
    private static readonly Uri DefaultProviderUri = new("https://replicate.com/");
    private const string DefaultPrefer = "wait=60";
    private const string DefaultOutputMediaType = "image/webp";

    public string ProviderKey => "replicate";
    public string DisplayName => "Replicate";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("replicate:ApiKey", new[] { "REPLICATE_API_KEY", "REPLICATE_API_TOKEN" }),
        };

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    public Meai.IImageGenerator CreateImageGenerator(ProviderClientConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var modelName = string.IsNullOrWhiteSpace(config.ModelName)
            ? DefaultModel
            : config.ModelName;
        var replicateConfig = config.ProviderConfig as ReplicateProviderConfig;
        var replicateOptions = (config as ImageGenerationClientConfig)?.ProviderOptions as ReplicateImageOptions;
        var mediaType = (config as ImageGenerationClientConfig)?.MediaType;
        var model = ParseModel(modelName!, replicateConfig?.ModelOwner);
        var client = CreateReplicateClient(config, services);

        return new ReplicateImageGenerator(client, model.Owner, model.Name, replicateOptions, mediaType);
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Generated provider payload contracts are AOT-compatible.")]
    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family != ProviderClientFamily.ImageGeneration)
            errors.Add("Replicate currently supports only image generation in HPD Agent.");


        var replicateConfig = config.ProviderConfig as ReplicateProviderConfig;
        var modelName = string.IsNullOrWhiteSpace(config.ModelName) ? DefaultModel : config.ModelName;
        if (replicateConfig?.ModelOwner is { Length: 0 })
            errors.Add("ModelOwner cannot be empty");
        ValidateModel(modelName!, replicateConfig?.ModelOwner, errors);

        if (!string.IsNullOrWhiteSpace(config.Endpoint) &&
            !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
        {
            errors.Add("Endpoint must be a valid, absolute URI");
        }

        if ((config as ImageGenerationClientConfig)?.ProviderOptions is ReplicateImageOptions options)
            ValidateProviderOptions(options, errors);

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    internal static void ValidateProviderOptions(ReplicateImageOptions config, List<string> errors)
    {
        if (config.Prefer is { Length: 0 })
            errors.Add("Prefer cannot be empty");

        if (config.TimeoutSeconds.HasValue && config.TimeoutSeconds.Value <= 0)
            errors.Add("TimeoutSeconds must be greater than 0");

        if (config.PollingIntervalSeconds.HasValue && config.PollingIntervalSeconds.Value <= 0)
            errors.Add("PollingIntervalSeconds must be greater than 0");

        if (config.Input is not null)
        {
            foreach (var key in config.Input.Keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    errors.Add("Input keys cannot be empty");
            }
        }
    }

    private static global::Replicate.ReplicateClient CreateReplicateClient(ProviderClientConfig config, IServiceProvider? services)
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
        private readonly ReplicateImageOptions? _options;
        private readonly string? _mediaType;
        private Meai.ImageGeneratorMetadata? _metadata;

        public ReplicateImageGenerator(
            global::Replicate.ReplicateClient client,
            string modelOwner,
            string modelName,
            ReplicateImageOptions? options,
            string? mediaType)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _modelOwner = modelOwner;
            _modelName = modelName;
            _options = options;
            _mediaType = mediaType;
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

            var input = CreateInput(request, options, _options);
            var prefer = GetAdditionalString(options, "replicate:prefer") ?? _options?.Prefer ?? DefaultPrefer;
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
                    TimeSpan.FromSeconds(_options?.PollingIntervalSeconds ?? 2),
                    TimeSpan.FromSeconds(_options?.TimeoutSeconds ?? 120),
                    cancellationToken).ConfigureAwait(false);
            }

            var result = new Meai.ImageGenerationResponse(CreateContents(response.Output, options, _mediaType))
            {
                RawRepresentation = response
            };
            return result;
        }

        private static Dictionary<string, object?> CreateInput(
            Meai.ImageGenerationRequest request,
            Meai.ImageGenerationOptions? options,
            ReplicateImageOptions? config)
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
            string? configuredMediaType)
        {
            var mediaType = options?.MediaType ?? configuredMediaType ?? DefaultOutputMediaType;
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
