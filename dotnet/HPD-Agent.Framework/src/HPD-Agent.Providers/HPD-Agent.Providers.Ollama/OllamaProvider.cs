using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Text.Json;
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
/// Provider-specific acquisition settings are stored in <c>Clients.Chat.ProviderConfig</c>
/// and validated as <see cref="OllamaProviderConfig"/> through generated composition.
/// </para>
/// </remarks>
[HpdProvider("ollama", "Ollama", DocumentationUrl = "https://ollama.com/")]
[HpdProviderBackend("local", ProviderAuthenticationKind.Anonymous, IsDefaultBackend = true, IsDefaultAuthentication = true)]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(OllamaProviderConfig), typeof(OllamaJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.OperationOptions, typeof(OllamaChatRequestOptions), typeof(OllamaJsonContext))]
[HpdProviderSecretAlias("ollama:Endpoint", "OLLAMA_ENDPOINT")]
[HpdProviderSecretAlias("ollama:Host", "OLLAMA_HOST")]
internal class OllamaProvider : IProvider, IProviderClientFactory<IChatClient>, IProviderSecretAliasProvider
{
    public string ProviderKey => "ollama";
    public string DisplayName => "Ollama";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("ollama:Endpoint", new[] { "OLLAMA_ENDPOINT" }),
            new("ollama:Host", new[] { "OLLAMA_HOST" }),
        };

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public ProviderClientCredentialBinding ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    public ValueTask<ProviderClientConstruction<IChatClient>> CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        ProviderClientConstructionUtilities.RequireAnonymous(context.CredentialBinding);
        var config = context.EffectiveConfig;
        var endpoint = config.Endpoint ?? new Uri("http://localhost:11434");

        if (string.IsNullOrEmpty(config.ModelName))
        {
            throw new InvalidOperationException("Model name is required for Ollama provider.");
        }

        var providerConfig = ReadConfig(config);

        var httpClient = context.Services.HttpClientFactory.CreateClient("hpd-provider-ollama");
        httpClient.BaseAddress = endpoint;
        if (providerConfig?.TimeoutMs is { } timeoutMs)
        {
            httpClient.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
        }

        var client = new OllamaApiClient(httpClient, config.ModelName);

        if (config.CustomHeaders.Count > 0)
        {
            foreach (var header in config.CustomHeaders)
            {
                client.DefaultRequestHeaders[header.Key] = header.Value;
            }
        }

        IChatClient configured = new OllamaConfiguredChatClient(client, config.ModelName, endpoint, providerConfig);
        return ValueTask.FromResult(new ProviderClientConstruction<IChatClient>
        {
            Client = configured,
            Owner = ProviderClientConstructionUtilities.Own(httpClient, configured)
        });
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
    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        var errors = new List<string>();

        if (config.Family != ProviderClientFamily.Chat)
            errors.Add("Ollama supports only chat.");

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name is required for Ollama");

        var providerConfig = ReadConfig(config);
        if (providerConfig?.TimeoutMs is <= 0)
            errors.Add("TimeoutMs must be greater than zero when specified.");

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    private static OllamaProviderConfig? ReadConfig(EffectiveProviderClientConfig config) =>
        config.ProviderConfiguration.CanonicalPayload.IsEmpty
            ? null
            : JsonSerializer.Deserialize(
                config.ProviderConfiguration.CanonicalPayload.AsSpan(),
                OllamaJsonContext.Default.OllamaProviderConfig);
}
