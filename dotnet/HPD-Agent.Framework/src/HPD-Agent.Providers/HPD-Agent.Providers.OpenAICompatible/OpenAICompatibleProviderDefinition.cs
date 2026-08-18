using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.OpenAICompatible;

/// <summary>
/// Static provider metadata needed to build a small OpenAI-compatible chat provider.
/// </summary>
public sealed class OpenAICompatibleProviderDefinition
{
    public required string ProviderKey { get; init; }
    public required string DisplayName { get; init; }
    public required Uri DefaultEndpoint { get; init; }
    public required string DefaultModelId { get; init; }
    public required string ApiKeySecretKey { get; init; }
    public string? EndpointSecretKey { get; init; }

    /// <summary>
    /// Environment variables used to resolve <see cref="ApiKeySecretKey"/> at runtime.
    /// Parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute so that
    /// explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public string[] ApiKeyEnvironmentVariables { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Environment variables used to resolve <see cref="EndpointSecretKey"/> at runtime.
    /// Parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute.
    /// </summary>
    public string[] EndpointEnvironmentVariables { get; init; } = Array.Empty<string>();
    public Uri? ProviderUri { get; init; }
    public Uri? DocumentationUri { get; init; }
    public string ChatCompletionsPath { get; init; } = "chat/completions";
    public bool RequiresApiKey { get; init; } = true;

    /// <summary>Gets or initializes the optional request fields supported by the provider.</summary>
    public OpenAICompatibleRequestProfile RequestProfile { get; init; } = new();
    public IReadOnlyDictionary<string, object?> Capabilities { get; init; } =
        new Dictionary<string, object?>();
}
