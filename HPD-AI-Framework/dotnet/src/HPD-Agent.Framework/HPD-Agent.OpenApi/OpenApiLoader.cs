using Microsoft.Extensions.AI;
using HPD.OpenApi.Core;

namespace HPD.Agent.OpenApi;

/// <summary>
/// Implements <see cref="IOpenApiLoader"/> — bridges the core hook to actual parsing
/// and AIFunction creation. Config is cast from <see cref="object"/> here, not in AgentBuilder.
///
/// The loader owns all config interpretation, HttpClient lifecycle decisions, and function
/// creation. AgentBuilder passes raw registrations through and collects the results.
/// </summary>
internal sealed class OpenApiLoader : IOpenApiLoader
{
    public async Task<OpenApiLoadResult> LoadAllAsync(
        IReadOnlyList<OpenApiSourceRegistration> sources,
        CancellationToken cancellationToken)
    {
        var allFunctions = new List<AIFunction>();
        var ownedClients = new List<HttpClient>();

        foreach (var source in sources)
        {
            var config = (OpenApiConfig)source.Config;

            // Upfront mutual exclusion validation
            if (config.SpecPath != null && config.SpecUri != null)
                throw new ArgumentException(
                    $"OpenAPI source '{source.Name}': SpecPath and SpecUri are mutually exclusive.");
            if (config.SpecPath == null && config.SpecUri == null)
                throw new ArgumentException(
                    $"OpenAPI source '{source.Name}': Either SpecPath or SpecUri must be provided.");
            if (config.OperationSelectionPredicate != null && config.OperationsToExclude is { Count: > 0 })
                throw new ArgumentException(
                    $"OpenAPI source '{source.Name}': " +
                    "OperationSelectionPredicate and OperationsToExclude cannot be used together.");

            // Resolve HttpClient: user-provided clients are used as-is (never disposed by us).
            // Internally-created clients are added to ownedClients for disposal with the Agent.
            HttpClient httpClient;
            if (config.HttpClient != null)
            {
                httpClient = config.HttpClient;
            }
            else
            {
                var owned = new HttpClient { Timeout = config.Timeout };
                ownedClients.Add(owned);
                httpClient = owned;
            }

            var functions = await LoadSourceAsync(source, config, httpClient, cancellationToken);
            allFunctions.AddRange(functions);
        }

        return new OpenApiLoadResult
        {
            Functions = allFunctions,
            OwnedHttpClients = ownedClients
        };
    }

    private static async Task<List<AIFunction>> LoadSourceAsync(
        OpenApiSourceRegistration source,
        OpenApiConfig config,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        HPD.OpenApi.Core.Model.ParsedOpenApiSpec spec;
        try
        {
            spec = await OpenApiSpecLoader.LoadAndParseAsync(config, httpClient, cancellationToken);
        }
        catch (OpenApiParseException)
        {
            // Re-throw — the caller sees the parse error with the source name for context.
            // Spec parse errors are fatal: a broken spec produces zero functions and
            // the agent should fail fast rather than silently skip all operations.
            throw;
        }

        if (spec.Operations.Count == 0)
        {
            // No operations matched — this is almost always a misconfigured predicate or
            // exclude list. Return empty rather than failing so other sources still load.
            System.Diagnostics.Debug.WriteLine(
                $"[HPD-Agent.OpenApi] Warning: OpenAPI source '{source.Name}' produced no operations " +
                $"after filtering. Check OperationSelectionPredicate / OperationsToExclude.");
            return [];
        }

        var runner = OpenApiSpecLoader.CreateRunner(config, httpClient);

        // CollapseWithinToolkit is read from config (authoritative) rather than from source.CollapseWithinToolkit.
        // source.CollapseWithinToolkit is a placeholder for registrations created by AgentBuilder
        // (which cannot cast config to OpenApiConfig), so the value in config takes precedence.
        return OpenApiFunctionFactory.CreateFunctions(
            spec,
            config,
            runner,
            namePrefix: source.Name,
            parentContainer: source.ParentContainer,
            collapseWithinToolkit: config.CollapseWithinToolkit);
    }
}
