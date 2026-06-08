using FluentAssertions;
using HPD.Agent.TUI;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.ModelsDev.Tests;

public sealed class ModelsDevAgentTuiModelCatalogTests
{
    [Fact]
    public async Task GetProvidersAsync_maps_provider_state()
    {
        var catalog = CreateCatalog(new StaticModelsDevProviderState(
            new Dictionary<string, ModelsDevProviderStatus>(StringComparer.OrdinalIgnoreCase)
            {
                ["openai"] = new(true, true),
                ["openrouter"] = new(true, false)
            }));

        var providers = await catalog.GetProvidersAsync(Context());

        providers.Should().Contain(p => p.ProviderKey == "openai"
            && p.IsRegistered
            && p.IsAuthenticated);
        providers.Should().Contain(p => p.ProviderKey == "openrouter"
            && p.IsRegistered
            && !p.IsAuthenticated);
    }

    [Fact]
    public async Task GetModelsAsync_filters_to_chat_models_and_skips_deprecated_embeddings()
    {
        var catalog = CreateCatalog();

        var models = await catalog.GetModelsAsync(Context(), "openai", new AgentTuiModelQuery());

        models.Should().ContainSingle();
        models[0].ModelId.Should().Be("gpt-4o");
        models[0].DisplayName.Should().Be("GPT-4o");
        models[0].SupportsTools.Should().BeTrue();
    }

    [Fact]
    public async Task GetModelsAsync_supports_free_filter_and_search()
    {
        var catalog = CreateCatalog();

        var free = await catalog.GetModelsAsync(Context(), "openrouter", new AgentTuiModelQuery(FreeOnly: true));
        var searched = await catalog.GetModelsAsync(Context(), "openrouter", new AgentTuiModelQuery(Search: "deepseek"));

        free.Should().ContainSingle(m => m.ModelId == "deepseek/deepseek-chat");
        searched.Should().ContainSingle(m => m.ModelId == "deepseek/deepseek-chat");
    }

    [Fact]
    public async Task GetModelsAsync_returns_empty_for_unmapped_provider()
    {
        var catalog = CreateCatalog();

        var models = await catalog.GetModelsAsync(Context(), "unknown", new AgentTuiModelQuery());

        models.Should().BeEmpty();
    }

    private static ModelsDevAgentTuiModelCatalog CreateCatalog(
        IModelsDevProviderState? providerState = null)
        => new(
            ModelsDevStore.FromDatabase(Database()),
            providerState ?? new StaticModelsDevProviderState(
                new Dictionary<string, ModelsDevProviderStatus>(StringComparer.OrdinalIgnoreCase)
                {
                    ["openai"] = new(true, true),
                    ["openrouter"] = new(true, true)
                }));

    private static AgentTuiModelCatalogContext Context()
    {
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        return new AgentTuiModelCatalogContext(scope, new ChatShellModel(scope));
    }

    private static ModelsDevDatabase Database()
        => new()
        {
            Providers = new Dictionary<string, ModelsDevProvider>(StringComparer.OrdinalIgnoreCase)
            {
                ["openai"] = new()
                {
                    Models = new Dictionary<string, ModelsDevModel>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["gpt-4o"] = new()
                        {
                            Name = "GPT-4o",
                            Family = "gpt",
                            ToolCall = true,
                            Cost = new ModelsDevCost { Input = 2.5m, Output = 10m },
                            Modalities = new ModelsDevModalities { Output = ["text"] }
                        },
                        ["text-embedding-3-small"] = new()
                        {
                            Name = "Text Embedding 3 Small",
                            Family = "embed",
                            Modalities = new ModelsDevModalities { Output = ["embedding"] }
                        },
                        ["old-chat"] = new()
                        {
                            Name = "Old Chat",
                            Status = "deprecated",
                            Modalities = new ModelsDevModalities { Output = ["text"] }
                        }
                    }
                },
                ["openrouter"] = new()
                {
                    Models = new Dictionary<string, ModelsDevModel>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["deepseek/deepseek-chat"] = new()
                        {
                            Name = "DeepSeek Chat",
                            Family = "deepseek",
                            ToolCall = true,
                            Cost = new ModelsDevCost { Input = 0m, Output = 0m },
                            Modalities = new ModelsDevModalities { Output = ["text"] }
                        },
                        ["paid-chat"] = new()
                        {
                            Name = "Paid Chat",
                            Family = "paid",
                            Cost = new ModelsDevCost { Input = 1m, Output = 2m },
                            Modalities = new ModelsDevModalities { Output = ["text"] }
                        }
                    }
                }
            }
        };
}
