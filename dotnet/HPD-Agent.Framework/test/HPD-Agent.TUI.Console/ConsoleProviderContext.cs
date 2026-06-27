using System.Reflection;
using System.Runtime.CompilerServices;
using HPD.Agent.ModelsDev;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Anthropic;
using HPD.Agent.Providers.AzureAI;
using HPD.Agent.Providers.Bedrock;
using HPD.Agent.Providers.DashScope;
using HPD.Agent.Providers.GoogleAI;
using HPD.Agent.Providers.HuggingFace;
using HPD.Agent.Providers.Mistral;
using HPD.Agent.Providers.Ollama;
using HPD.Agent.Providers.OpenAI;
using HPD.Agent.Providers.OpenRouter;
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
        LoadConsoleProviders();

        var configuration = BuildConfiguration();
        var secrets = new ChainedSecretResolver(
            new EnvironmentSecretResolver(),
            new ConfigurationSecretResolver(configuration));

        var registryProbe = new AgentBuilder()
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
            KnownProviders);
    }

    public AgentBuilder CreateAgentBuilder()
        => new AgentBuilder()
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

    private static void LoadConsoleProviders()
    {
        LoadProviderModule(typeof(AnthropicProviderModule));
        LoadProviderModule(typeof(AzureAIProviderModule));
        LoadProviderModule(typeof(BedrockProviderModule));
        LoadProviderModule(typeof(DashScopeProviderModule));
        LoadProviderModule(typeof(GoogleAIProviderModule));
        LoadProviderModule(typeof(HuggingFaceProviderModule));
        LoadProviderModule(typeof(MistralProviderModule));
        LoadProviderModule(typeof(OllamaProviderModule));
        LoadProviderModule(typeof(OpenAIProviderModule));
        LoadProviderModule(typeof(OpenRouterProviderModule));
    }

    private static void LoadProviderModule(Type moduleType)
        => RuntimeHelpers.RunModuleConstructor(moduleType.Module.ModuleHandle);

    private static readonly ConsoleProviderMetadata[] KnownProviders =
    [
        new("anthropic", "Anthropic", ["anthropic:ApiKey"]),
        new("azure-openai", "Azure OpenAI", ["azure-openai:ApiKey", "azure-openai:Endpoint"]),
        new("azure-ai", "Azure AI", ["azure-ai:ApiKey", "azure-ai:Endpoint"]),
        new("bedrock", "Amazon Bedrock", ["bedrock:AccessKeyId", "bedrock:SecretAccessKey"]),
        new("dashscope", "DashScope", ["dashscope:ApiKey"]),
        new("google-ai", "Google AI", ["google-ai:ApiKey"]),
        new("huggingface", "Hugging Face", ["huggingface:ApiKey"]),
        new("mistral", "Mistral", ["mistral:ApiKey"]),
        new("ollama", "Ollama", []),
        new("openai", "OpenAI", ["openai:ApiKey"]),
        new("openrouter", "OpenRouter", ["openrouter:ApiKey"])
    ];
}

internal sealed record ConsoleProviderMetadata(
    string ProviderKey,
    string DisplayName,
    IReadOnlyList<string> RequiredSecretKeys);
