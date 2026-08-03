using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using OllamaSharp;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

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
/// Provider-specific options are stored in ConstructionOptions and validated through
/// OllamaProviderConfig.
/// </para>
/// </remarks>
[HpdProvider("ollama", "Ollama", DocumentationUrl = "https://ollama.com/")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(OllamaProviderConfig), typeof(OllamaJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.OperationOptions, typeof(OllamaChatRequestOptions), typeof(OllamaJsonContext))]
[HpdProviderSecretAlias("ollama:Endpoint", "OLLAMA_ENDPOINT")]
[HpdProviderSecretAlias("ollama:Host", "OLLAMA_HOST")]
internal class OllamaProvider : IChatClientProvider
{
    public string ProviderKey => "ollama";
    public string DisplayName => "Ollama";

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        var secrets = services?.GetService<ISecretResolver>();
        var resolvedEndpoint = config.Endpoint
            ?? ResolveOptionalSecret(secrets, "ollama:Endpoint")
            ?? ResolveOptionalSecret(secrets, "ollama:Host");

        // Resolve endpoint - defaults to localhost if not provided
        var endpoint = string.IsNullOrEmpty(resolvedEndpoint)
            ? new Uri("http://localhost:11434")
            : new Uri(resolvedEndpoint);

        if (string.IsNullOrEmpty(config.ModelName))
        {
            throw new InvalidOperationException("Model name is required for Ollama provider.");
        }

        var providerConfig = config.ProviderConfig as OllamaProviderConfig;

        var httpClient = new HttpClient { BaseAddress = endpoint };
        if (providerConfig?.TimeoutMs is { } timeoutMs)
        {
            httpClient.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
        }

        var client = new OllamaApiClient(httpClient, config.ModelName);

        if (config.CustomHeaders is not null)
        {
            foreach (var header in config.CustomHeaders)
            {
                client.DefaultRequestHeaders[header.Key] = header.Value;
            }
        }

        return new OllamaConfiguredChatClient(client, config.ModelName, endpoint, providerConfig);
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
    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name is required for Ollama");

        if (!string.IsNullOrEmpty(config.Endpoint) && !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
            errors.Add("Endpoint must be a valid, absolute URI");

        var providerConfig = config.ProviderConfig as OllamaProviderConfig;
        if (providerConfig?.TimeoutMs is <= 0)
            errors.Add("TimeoutMs must be greater than zero when specified.");

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    private static string? ResolveOptionalSecret(ISecretResolver? secrets, string key)
        => secrets?.ResolveAsync(key, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult()
            ?.Value;
}
