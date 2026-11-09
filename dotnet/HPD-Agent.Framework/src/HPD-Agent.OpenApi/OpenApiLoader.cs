using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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
        ILogger logger,
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
            HttpClient? ownedClient = null;
            if (config.HttpClient != null)
            {
                httpClient = config.HttpClient;
            }
            else
            {
                ownedClient = new HttpClient { Timeout = config.Timeout };
                httpClient = ownedClient;
            }

            try
            {
                var functions = await LoadSourceAsync(
                    source,
                    config,
                    httpClient,
                    logger,
                    cancellationToken);
                if (ownedClient is not null)
                    ownedClients.Add(ownedClient);
                allFunctions.AddRange(functions);
            }
            catch
            {
                ownedClient?.Dispose();
                throw;
            }
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
        ILogger logger,
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
            return HandleEmptyCatalog(source, config, spec.Operations.Count, logger);

        var runner = OpenApiSpecLoader.CreateRunner(config, httpClient);

        // CollapseWithinToolHarness is read from config (authoritative) rather than from source.CollapseWithinToolHarness.
        // source.CollapseWithinToolHarness is a placeholder for registrations created by AgentBuilder
        // (which cannot cast config to OpenApiConfig), so the value in config takes precedence.
        var functions = OpenApiFunctionFactory.CreateFunctions(
            spec,
            config,
            runner,
            namePrefix: source.Name,
            parentContainer: source.ParentContainer,
            collapseWithinToolHarness: config.CollapseWithinToolHarness,
            logger: logger);
        return functions.Count == 0
            ? HandleEmptyCatalog(source, config, spec.Operations.Count, logger)
            : functions;
    }

    private static List<AIFunction> HandleEmptyCatalog(
        OpenApiSourceRegistration source,
        OpenApiConfig config,
        int remainingOperationCount,
        ILogger logger)
    {
        var exclusions = config.OperationsToExclude is { Count: > 0 }
            ? string.Join(", ", config.OperationsToExclude)
            : "none";
        var predicate = config.OperationSelectionPredicate is null
            ? "none"
            : "configured";

        logger.LogWarning(
            "OpenAPI source {SourceName} produced zero operations. " +
            "Operations remaining after filtering {OperationCount}, exclusions {Exclusions}, " +
            "selection predicate {SelectionPredicate}.",
            source.Name,
            remainingOperationCount,
            exclusions,
            predicate);

        if (config.AllowEmptyCatalog)
            return [];

        throw new AgentRunConfigurationException(
            "OpenApiSourceProducedNoOperations",
            $"openapi.sources.{source.Name}",
            $"OpenApiSourceProducedNoOperations: OpenAPI source '{source.Name}' produced no operations. " +
            $"Operations remaining after filtering: {remainingOperationCount}. " +
            $"Exclusions: {exclusions}. Selection predicate: {predicate}. " +
            "Check the specification and filters, or set AllowEmptyCatalog=true " +
            "when an empty source is intentional.");
    }
}
