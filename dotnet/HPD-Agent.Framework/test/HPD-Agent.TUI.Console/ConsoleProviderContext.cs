using System.Reflection;
using HPD.Agent.ModelsDev;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using HPD.Agent.TUI.Models;
using Microsoft.Extensions.Configuration;

namespace HPD.Agent.TUI.Console;

internal sealed class ConsoleProviderContext
{
    private ConsoleProviderContext(
        IConfiguration configuration,
        ISecretResolver secrets,
        IProviderRegistry providerRegistry,
        IModelsDevProviderState providerState,
        IAgentTuiModelCatalog modelCatalog,
        AgentTuiModelSelectionState modelSelection,
        IReadOnlyList<ConsoleProviderMetadata> providers)
    {
        Configuration = configuration;
        Secrets = secrets;
        ProviderRegistry = providerRegistry;
        ProviderState = providerState;
        ModelCatalog = modelCatalog;
        ModelSelection = modelSelection;
        Providers = providers;
    }

    public IConfiguration Configuration { get; }

    public ISecretResolver Secrets { get; }

    public IProviderRegistry ProviderRegistry { get; }

    public IModelsDevProviderState ProviderState { get; }

    public IAgentTuiModelCatalog ModelCatalog { get; }

    public AgentTuiModelSelectionState ModelSelection { get; }

    public IReadOnlyList<ConsoleProviderMetadata> Providers { get; }

    public static ConsoleProviderContext Create()
    {
        var composition = HPD.Agent.Providers.Generated.GeneratedProviderComposition.Composition;
        var configuration = BuildConfiguration();
        var secrets = new ChainedSecretResolver(
            new EnvironmentSecretResolver(composition.SecretAliases),
            new ConfigurationSecretResolver(configuration));

        var registryProbe = new AgentBuilder(composition)
            .WithAPIConfiguration(configuration);

        var providerState = new HpdModelsDevProviderState(registryProbe.ProviderRegistry, secrets);
        var modelCatalog = new ConsoleModelsDevModelCatalog(
            new ModelsDevStore(new HttpClient()),
            providerState,
            ModelsDevProviderMappings.Default);

        return new ConsoleProviderContext(
            configuration,
            secrets,
            registryProbe.ProviderRegistry,
            providerState,
            modelCatalog,
            new AgentTuiModelSelectionState(),
            CreateProviderMetadata(composition));
    }

    public AgentBuilder CreateAgentBuilder()
        => new AgentBuilder(HPD.Agent.Providers.Generated.GeneratedProviderComposition.Composition)
            .WithAPIConfiguration(Configuration);

    private static IConfiguration BuildConfiguration()
    {
        var basePath = AppContext.BaseDirectory;
        var currentPath = Directory.GetCurrentDirectory();

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        if (!string.Equals(basePath, currentPath, StringComparison.Ordinal))
        {
            builder.AddJsonFile(
                Path.Combine(currentPath, "appsettings.json"),
                optional: true,
                reloadOnChange: true);
        }

        if (Assembly.GetEntryAssembly() is { } entryAssembly)
        {
            builder.AddUserSecrets(entryAssembly, optional: true, reloadOnChange: true);
        }

        return builder
            .AddEnvironmentVariables()
            .Build();
    }

    private static IReadOnlyList<ConsoleProviderMetadata> CreateProviderMetadata(
        ProviderComposition composition)
    {
        var secretKeys = composition.Fragments
            .SelectMany(static fragment => fragment.SecretAliases)
            .Select(static alias => alias.SecretKey)
            .ToArray();
        return composition.Descriptors.Providers
            .Where(static descriptor => descriptor.Families.ContainsKey(ProviderClientFamily.Chat))
            .Select(descriptor => new ConsoleProviderMetadata(
                descriptor.ProviderKey,
                descriptor.DisplayName,
                secretKeys.Where(key => key.StartsWith(
                    descriptor.ProviderKey + ":",
                    StringComparison.Ordinal)).ToArray()))
            .ToArray();
    }
}

internal sealed record ConsoleProviderMetadata(
    string ProviderKey,
    string DisplayName,
    IReadOnlyList<string> RequiredSecretKeys);
