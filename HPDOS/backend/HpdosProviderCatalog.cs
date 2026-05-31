using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.Configuration;

internal static class HpdosProviderCatalogEndpoints
{
    public static void MapHpdosProviderCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/hpdos/providers", (IServiceProvider services) =>
        {
            try
            {
                return Results.Ok(ListProviders(services));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/api/hpdos/providers/{providerKey}", async (
            string providerKey,
            IServiceProvider services,
            IConfiguration configuration,
            HpdosProviderCredentialStore credentials,
            CancellationToken ct) =>
        {
            try
            {
                var provider = ResolveProviderRegistry(services).GetProvider(providerKey);
                if (provider is null)
                    return Results.NotFound(new { error = $"Provider '{providerKey}' is not registered." });

                var item = ToCatalogItem(provider);
                var status = await GetProviderStatusAsync(item, configuration, credentials, ct);
                return Results.Ok(new HpdosProviderDetail(item, status));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/api/hpdos/providers/{providerKey}/status", async (
            string providerKey,
            IServiceProvider services,
            IConfiguration configuration,
            HpdosProviderCredentialStore credentials,
            CancellationToken ct) =>
        {
            try
            {
                var provider = ResolveProviderRegistry(services).GetProvider(providerKey);
                if (provider is null)
                    return Results.NotFound(new { error = $"Provider '{providerKey}' is not registered." });

                var item = ToCatalogItem(provider);
                return Results.Ok(await GetProviderStatusAsync(item, configuration, credentials, ct));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPut("/api/hpdos/providers/{providerKey}/credential", async (
            string providerKey,
            HpdosProviderCredentialRequest request,
            IServiceProvider services,
            IConfiguration configuration,
            HpdosProviderCredentialStore credentials,
            CancellationToken ct) =>
        {
            try
            {
                var provider = ResolveProviderRegistry(services).GetProvider(providerKey);
                if (provider is null)
                    return Results.NotFound(new { error = $"Provider '{providerKey}' is not registered." });

                var item = ToCatalogItem(provider);
                if (string.Equals(item.Auth.Kind, "local", StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest(new { error = $"Provider '{providerKey}' does not require a stored credential." });

                var key = ProviderCredentialKey(provider.ProviderKey, request.SecretName);
                await credentials.SaveCredentialAsync(key, request.Value, ct);

                return Results.Ok(await GetProviderStatusAsync(item, configuration, credentials, ct));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapDelete("/api/hpdos/providers/{providerKey}/credential", async (
            string providerKey,
            string? secretName,
            IServiceProvider services,
            IConfiguration configuration,
            HpdosProviderCredentialStore credentials,
            CancellationToken ct) =>
        {
            try
            {
                var provider = ResolveProviderRegistry(services).GetProvider(providerKey);
                if (provider is null)
                    return Results.NotFound(new { error = $"Provider '{providerKey}' is not registered." });

                var item = ToCatalogItem(provider);
                var key = ProviderCredentialKey(provider.ProviderKey, secretName);
                await credentials.DeleteCredentialAsync(key, ct);

                return Results.Ok(await GetProviderStatusAsync(item, configuration, credentials, ct));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/api/hpdos/models", async (
            IServiceProvider services,
            HpdosModelCatalogService models,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await ListModelsAsync(services, models, ct));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPut("/api/hpdos/providers/{providerKey}/models/{modelId}", async (
            string providerKey,
            string modelId,
            HpdosCustomModelRequest request,
            IServiceProvider services,
            HpdosCustomModelStore customModels,
            CancellationToken ct) =>
        {
            try
            {
                var provider = ResolveProviderRegistry(services).GetProvider(providerKey);
                if (provider is null)
                    return Results.NotFound(new { error = $"Provider '{providerKey}' is not registered." });

                var model = await customModels.UpsertAsync(new HpdosCustomModel(
                    ProviderKey: provider.ProviderKey,
                    ModelId: modelId,
                    DisplayName: request.DisplayName,
                    Family: request.Family,
                    Tools: request.Tools,
                    Reasoning: request.Reasoning,
                    Vision: request.Vision,
                    Audio: request.Audio,
                    Attachments: request.Attachments,
                    Local: request.Local,
                    Free: request.Free), ct);

                return Results.Ok(model);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapDelete("/api/hpdos/providers/{providerKey}/models/{modelId}", async (
            string providerKey,
            string modelId,
            IServiceProvider services,
            HpdosCustomModelStore customModels,
            CancellationToken ct) =>
        {
            try
            {
                var provider = ResolveProviderRegistry(services).GetProvider(providerKey);
                if (provider is null)
                    return Results.NotFound(new { error = $"Provider '{providerKey}' is not registered." });

                var removed = await customModels.DeleteAsync(provider.ProviderKey, modelId, ct);
                return removed
                    ? Results.NoContent()
                    : Results.NotFound(new { error = $"Model '{modelId}' is not configured for provider '{providerKey}'." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static List<HpdosProviderCatalogItem> ListProviders(IServiceProvider services)
    {
        var registry = ResolveProviderRegistry(services);

        return registry.GetRegisteredProviders()
            .Select(registry.GetProvider)
            .Where(provider => provider is not null)
            .Select(provider => ToCatalogItem(provider!))
            .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Task<List<HpdosModelCatalogItem>> ListModelsAsync(
        IServiceProvider services,
        HpdosModelCatalogService models,
        CancellationToken ct)
    {
        var registeredProviders = ResolveProviderRegistry(services)
            .GetRegisteredProviders()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return models.ListModelsAsync(registeredProviders, ct);
    }

    private static IProviderRegistry ResolveProviderRegistry(IServiceProvider services)
        => services.GetService<IProviderRegistry>() ?? new AgentBuilder().ProviderRegistry;

    private static async Task<HpdosProviderStatus> GetProviderStatusAsync(
        HpdosProviderCatalogItem provider,
        IConfiguration configuration,
        HpdosProviderCredentialStore credentials,
        CancellationToken ct)
    {
        if (string.Equals(provider.Auth.Kind, "local", StringComparison.OrdinalIgnoreCase))
        {
            return new HpdosProviderStatus(
                ProviderKey: provider.ProviderKey,
                Connected: false,
                Source: "missing",
                Removable: false,
                HasLocalCredential: false,
                Message: "Local provider needs endpoint/runtime setup.");
        }

        var key = ProviderCredentialKey(provider.ProviderKey, null);
        var environment = await new EnvironmentSecretResolver().ResolveAsync(key, ct);
        var local = await credentials.ResolveAsync(key, ct);
        var config = await new ConfigurationSecretResolver(configuration).ResolveAsync(key, ct);
        var active = environment ?? local ?? config;

        return new HpdosProviderStatus(
            ProviderKey: provider.ProviderKey,
            Connected: active.HasValue,
            Source: active.HasValue ? SourceKind(active.Value.Source) : "missing",
            Removable: local.HasValue,
            HasLocalCredential: local.HasValue,
            Message: active.HasValue ? null : "Provider credential is missing.");
    }

    private static string ProviderCredentialKey(string providerKey, string? secretName)
    {
        var name = string.IsNullOrWhiteSpace(secretName) ? "ApiKey" : secretName.Trim();
        return $"{providerKey}:{name}";
    }

    private static string SourceKind(string source)
    {
        if (source.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            return "environment";
        if (source.StartsWith("config:", StringComparison.OrdinalIgnoreCase))
            return "configuration";
        if (source.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            return "local";
        return "unknown";
    }

    private static HpdosProviderCatalogItem ToCatalogItem(IProvider provider)
    {
        var metadata = provider.GetMetadata();
        var chatFamily = metadata.Families.TryGetValue(ProviderClientFamily.Chat, out var chat)
            ? chat
            : null;
        var chatCaps = chatFamily?.Capabilities;

        return new HpdosProviderCatalogItem(
            ProviderKey: provider.ProviderKey,
            DisplayName: string.IsNullOrWhiteSpace(metadata.DisplayName)
                ? provider.DisplayName
                : metadata.DisplayName,
            DocumentationUrl: metadata.DocumentationUri?.ToString(),
            Capabilities: new HpdosProviderCapability(
                Streaming: chatCaps?.TryGetValue("SupportsStreaming", out var s) == true && s is true,
                ToolCalling: chatCaps?.TryGetValue("SupportsFunctionCalling", out var f) == true && f is true,
                Vision: chatCaps?.TryGetValue("SupportsVision", out var v) == true && v is true,
                Audio: chatCaps?.TryGetValue("SupportsAudio", out var a) == true && a is true),
            Auth: HpdosProviderAuthDescriptor.ForProvider(provider.ProviderKey),
            ConfigurationFields: HpdosProviderAuthDescriptor.ConfigurationFieldsForProvider(provider.ProviderKey));
    }

}

internal sealed record HpdosProviderCatalogItem(
    string ProviderKey,
    string DisplayName,
    string? DocumentationUrl,
    HpdosProviderCapability Capabilities,
    HpdosProviderAuthDescriptor Auth,
    IReadOnlyList<HpdosProviderConfigField> ConfigurationFields);

internal sealed record HpdosProviderDetail(
    HpdosProviderCatalogItem Provider,
    HpdosProviderStatus Status);

internal sealed record HpdosProviderStatus(
    string ProviderKey,
    bool Connected,
    string Source,
    bool Removable,
    bool HasLocalCredential,
    string? Message);

internal sealed record HpdosProviderCredentialRequest(
    string Value,
    string? SecretName);

internal sealed record HpdosProviderCapability(
    bool Streaming,
    bool ToolCalling,
    bool Vision,
    bool Audio);

internal sealed record HpdosProviderAuthDescriptor(
    string Kind,
    bool Required,
    IReadOnlyList<string> Sources)
{
    public static HpdosProviderAuthDescriptor ForProvider(string providerKey)
    {
        var kind = string.Equals(providerKey, "ollama", StringComparison.OrdinalIgnoreCase)
            ? "local"
            : "apiKey";

        return new HpdosProviderAuthDescriptor(
            kind,
            Required: kind != "local",
            Sources: kind == "local"
                ? ["local", "configuration"]
                : ["environment", "configuration", "runtime"]);
    }

    public static IReadOnlyList<HpdosProviderConfigField> ConfigurationFieldsForProvider(string providerKey)
    {
        if (string.Equals(providerKey, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new HpdosProviderConfigField(
                    Key: "Endpoint",
                    Label: "Endpoint",
                    Kind: "url",
                    Required: false,
                    Description: "Optional local Ollama endpoint override.")
            ];
        }

        return
        [
            new HpdosProviderConfigField(
                Key: "ApiKey",
                Label: "API key",
                Kind: "secret",
                Required: true,
                Description: "Stored by the HPD-OS backend and resolved through HPD-Agent secrets.")
        ];
    }
}

internal sealed record HpdosProviderConfigField(
    string Key,
    string Label,
    string Kind,
    bool Required,
    string? Description,
    IReadOnlyList<string>? Options = null);

internal sealed record HpdosModelCatalogItem(
    string ProviderKey,
    string ModelId,
    string DisplayName,
    string? Family,
    string? ReleaseDate,
    string Status,
    HpdosModelCapability Capabilities,
    HpdosModelLimits? Limits,
    HpdosModelCost? Cost,
    IReadOnlyList<HpdosProviderConfigField> ProviderOptionsSchema,
    bool Free,
    bool Recommended);

internal sealed record HpdosModelCapability(
    bool Tools,
    bool Reasoning,
    bool Vision,
    bool Audio,
    bool Attachments,
    bool Local);

internal sealed record HpdosModelLimits(
    int? Context,
    int? Input,
    int? Output);

internal sealed record HpdosModelCost(
    decimal? Input,
    decimal? Output,
    decimal? CacheRead,
    decimal? CacheWrite);
