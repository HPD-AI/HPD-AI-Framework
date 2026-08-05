using FluentAssertions;
using HPD.Agent;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.TUI.Tests;

public sealed class ModelSelectionCommandTests
{
    [Fact]
    public async Task ModelCommand_OnlyUsesConnectedProviders()
    {
        var selection = new AgentTuiModelSelectionState();
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "disconnected",
                    "Disconnected",
                    IsRegistered: true,
                    IsAuthenticated: false),
                new AgentTuiProviderChoice(
                    "openrouter",
                    "OpenRouter",
                    IsRegistered: true,
                    IsAuthenticated: true)
            ],
            [
                new AgentTuiModelChoice(
                    "openrouter",
                    "deepseek/deepseek-chat",
                    "DeepSeek Chat",
                    IsRecommended: true,
                    Capabilities: new AgentTuiModelCapabilities(SupportsTools: true))
            ]);
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection)
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            new FirstChoiceDialogs(),
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        selection.Current.Should().NotBeNull();
        selection.Current!.ProviderKey.Should().Be("openrouter");
        selection.Current.ModelId.Should().Be("deepseek/deepseek-chat");

        var runConfig = registry.RunConfigComposer!(new AgentTuiRunConfigContext(
            scope,
            shell,
            "hello"));
        runConfig.Should().NotBeNull();
        runConfig!.Clients.Chat!.ProviderKey.Should().Be("openrouter");
        runConfig.Clients.Chat.ModelName.Should().Be("deepseek/deepseek-chat");
    }

    [Fact]
    public async Task ModelCommand_WithArguments_SetsSelectionDirectly()
    {
        var selection = new AgentTuiModelSelectionState();
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelectionCommand(new TestModelCatalog([], []), selection)
            .UseModelSelectionRunConfig(selection)
            .Build();
        registry.TryFindSlashCommand("/model openrouter model-a", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            new FirstChoiceDialogs(),
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        selection.Current.Should().BeEquivalentTo(new AgentTuiSelectedModel(
            "openrouter",
            "model-a",
            Capabilities: AgentTuiModelCapabilities.None));
    }

    [Fact]
    public async Task ModelCommand_ByDefaultCanShowModelsWithoutToolSupport()
    {
        var selection = new AgentTuiModelSelectionState();
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "openrouter",
                    "OpenRouter",
                    IsRegistered: true,
                    IsAuthenticated: true)
            ],
            [
                new AgentTuiModelChoice(
                    "openrouter",
                    "text-only",
                    "Text Only",
                    IsRecommended: true,
                    Capabilities: new AgentTuiModelCapabilities(SupportsTools: false)),
                new AgentTuiModelChoice(
                    "openrouter",
                    "tool-model",
                    "Tool Model",
                    Capabilities: new AgentTuiModelCapabilities(SupportsTools: true))
            ]);
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection)
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            new FirstChoiceDialogs(),
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        selection.Current.Should().NotBeNull();
        selection.Current!.ModelId.Should().Be("text-only");
        selection.Current.Capabilities!.SupportsTools.Should().BeFalse();
    }

    [Fact]
    public async Task ModelCommand_WhenConfiguredOnlyShowsToolCapableCatalogModels()
    {
        var selection = new AgentTuiModelSelectionState();
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "openrouter",
                    "OpenRouter",
                    IsRegistered: true,
                    IsAuthenticated: true)
            ],
            [
                new AgentTuiModelChoice(
                    "openrouter",
                    "text-only",
                    "Text Only",
                    IsRecommended: true,
                    Capabilities: new AgentTuiModelCapabilities(SupportsTools: false)),
                new AgentTuiModelChoice(
                    "openrouter",
                    "tool-model",
                    "Tool Model",
                    Capabilities: new AgentTuiModelCapabilities(SupportsTools: true))
            ]);
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection, configure: static options => options.RequireToolSupport = true)
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            new FirstChoiceDialogs(),
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        selection.Current.Should().NotBeNull();
        selection.Current!.ModelId.Should().Be("tool-model");
        selection.Current.Capabilities!.SupportsTools.Should().BeTrue();
    }

    [Fact]
    public async Task ModelCommand_BackAtModelSelectionReturnsToProviderSelection()
    {
        var selection = new AgentTuiModelSelectionState();
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "provider-a",
                    "Provider A",
                    IsRegistered: true,
                    IsAuthenticated: true),
                new AgentTuiProviderChoice(
                    "provider-b",
                    "Provider B",
                    IsRegistered: true,
                    IsAuthenticated: true)
            ],
            [
                new AgentTuiModelChoice(
                    "provider-a",
                    "gpt-a",
                    "Provider A Model",
                    IsRecommended: true,
                    Capabilities: new AgentTuiModelCapabilities(SupportsTools: true)),
                new AgentTuiModelChoice(
                    "provider-b",
                    "gpt-b",
                    "Provider B Model",
                    IsRecommended: true,
                    Capabilities: new AgentTuiModelCapabilities(SupportsTools: true))
            ]);

        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection)
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            new QueuedDialogs(
                selections:
                [
                    "Provider A",
                    null,
                    "Provider B",
                    "Provider B Model"
                ],
                inputs: []),
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        selection.Current.Should().NotBeNull();
        selection.Current!.ProviderKey.Should().Be("provider-b");
        selection.Current!.ModelId.Should().Be("gpt-b");
    }

    [Fact]
    public async Task ModelCommand_BackAtModelSelectionWithImplicitProviderClosesFlow()
    {
        var selection = new AgentTuiModelSelectionState();
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "openai",
                    "OpenAI",
                    IsRegistered: true,
                    IsAuthenticated: true)
            ],
            [
                new AgentTuiModelChoice(
                    "openai",
                    "gpt-5.5",
                    "GPT-5.5",
                    IsRecommended: true,
                    Capabilities: new AgentTuiModelCapabilities(SupportsTools: true))
            ]);
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection)
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);
        var dialogs = new QueuedDialogs(selections: [null], inputs: []);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            dialogs,
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        selection.Current.Should().BeNull();
        dialogs.SelectionCalls.Should().Be(1);
    }

    [Fact]
    public async Task ModelCommand_SearchFreeModelsUsesFilterableSelection()
    {
        var selection = new AgentTuiModelSelectionState();
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "openrouter",
                    "OpenRouter",
                    IsRegistered: true,
                    IsAuthenticated: true,
                    SupportsLiveModelSearch: true,
                    SupportsFreeModels: true)
            ],
            [
                new AgentTuiModelChoice(
                    "openrouter",
                    "paid-model",
                    "Paid Model",
                    IsRecommended: true,
                    IsFree: false,
                    Capabilities: new AgentTuiModelCapabilities(SupportsTools: true)),
                new AgentTuiModelChoice(
                    "openrouter",
                    "free-model",
                    "Free Model",
                    IsFree: true,
                    Capabilities: new AgentTuiModelCapabilities(SupportsTools: true))
            ]);
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection)
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);
        var dialogs = new QueuedDialogs(
            selections:
            [
                "Search free models",
                "Free Model (free-model) free"
            ],
            inputs: []);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            dialogs,
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        selection.Current.Should().NotBeNull();
        selection.Current!.ModelId.Should().Be("free-model");
        dialogs.InputCalls.Should().Be(0);
        dialogs.FilteredSelectionCalls.Should().Be(1);
        catalog.ModelQueries.Any(query => query.Live && query.FreeOnly && query.Search is null).Should().BeTrue();
    }

    [Fact]
    public async Task ModelCommand_BackFromSearchReturnsToProviderModelList()
    {
        var selection = new AgentTuiModelSelectionState();
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "openrouter",
                    "OpenRouter",
                    IsRegistered: true,
                    IsAuthenticated: true,
                    SupportsLiveModelSearch: true,
                    SupportsFreeModels: true)
            ],
            [
                new AgentTuiModelChoice(
                    "openrouter",
                    "paid-model",
                    "Paid Model",
                    IsRecommended: true,
                    IsFree: false,
                    Capabilities: new AgentTuiModelCapabilities(SupportsTools: true)),
                new AgentTuiModelChoice(
                    "openrouter",
                    "free-model",
                    "Free Model",
                    IsFree: true,
                    Capabilities: new AgentTuiModelCapabilities(SupportsTools: true))
            ]);
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection)
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);
        var dialogs = new QueuedDialogs(
            selections:
            [
                "Search free models",
                null,
                "Paid Model (paid-model) recommended"
            ],
            inputs: []);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            dialogs,
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        selection.Current.Should().NotBeNull();
        selection.Current!.ModelId.Should().Be("paid-model");
        dialogs.FilteredSelectionCalls.Should().Be(1);
        dialogs.SelectionCalls.Should().Be(3);
    }

    [Fact]
    public async Task ModelCommand_ConfiguresSelectionBeforeCommit()
    {
        var selection = new AgentTuiModelSelectionState();
        var configureSawUncommittedState = false;
        var commitSawFinalState = false;
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "openai",
                    "OpenAI",
                    IsRegistered: true,
                    IsAuthenticated: true)
            ],
            [
                new AgentTuiModelChoice(
                    "openai",
                    "gpt-5.5",
                    "GPT-5.5",
                    IsRecommended: true,
                    Capabilities: new AgentTuiModelCapabilities(
                        SupportsTools: true,
                        SupportsReasoning: true))
            ]);
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection, configure: options =>
            {
                options.ConfigureSelection = (_, model) =>
                {
                    configureSawUncommittedState = selection.Current is null;
                    return ValueTask.FromResult<AgentTuiSelectedModel?>(model with
                    {
                        Chat = new ChatClientConfig
                        {
                            Reasoning = new ReasoningOptions
                            {
                                Effort = ReasoningEffort.High
                            }
                        }
                    });
                };
                options.SelectionCommitted = (_, model) =>
                {
                    commitSawFinalState = selection.Current?.ProviderKey == model.ProviderKey
                        && selection.Current.ModelId == model.ModelId
                        && selection.Current.Chat?.Reasoning?.Effort == ReasoningEffort.High
                        && model.Chat?.Reasoning?.Effort == ReasoningEffort.High;
                    return ValueTask.CompletedTask;
                };
            })
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            new FirstChoiceDialogs(),
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        configureSawUncommittedState.Should().BeTrue();
        commitSawFinalState.Should().BeTrue();
        selection.Current.Should().NotBeNull();
        selection.Current!.Chat.Should().NotBeNull();
        selection.Current.Chat!.Reasoning!.Effort.Should().Be(ReasoningEffort.High);
    }

    [Fact]
    public async Task ModelCommand_CanConfigureReasoningEffort()
    {
        var selection = new AgentTuiModelSelectionState();
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "openai",
                    "OpenAI",
                    IsRegistered: true,
                    IsAuthenticated: true)
            ],
            [
                new AgentTuiModelChoice(
                    "openai",
                    "gpt-5.5",
                    "GPT-5.5",
                    IsRecommended: true,
                    Capabilities: new AgentTuiModelCapabilities(
                        SupportsTools: true,
                        SupportsReasoning: true))
            ]);
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection, configure: options =>
            {
                options.ConfigureSelection = (context, model) =>
                    AgentTuiModelConfigFlow.ConfigureAsync(context, model);
            })
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            new PreferredChoiceDialogs("High"),
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        selection.Current.Should().NotBeNull();
        selection.Current!.Chat?.Reasoning?.Effort.Should().Be(ReasoningEffort.High);
    }

    [Fact]
    public async Task ModelCommand_ConfiguresReasoningOnlyOnceBeforeCommit()
    {
        var selection = new AgentTuiModelSelectionState();
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "openai",
                    "OpenAI",
                    IsRegistered: true,
                    IsAuthenticated: true)
            ],
            [
                new AgentTuiModelChoice(
                    "openai",
                    "gpt-5.5",
                    "GPT-5.5",
                    IsRecommended: true,
                    Capabilities: new AgentTuiModelCapabilities(
                        SupportsTools: true,
                        SupportsReasoning: true))
            ]);
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection, configure: options =>
            {
                options.ConfigureSelection = (context, model) =>
                    AgentTuiModelConfigFlow.ConfigureAsync(context, model);
            })
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);
        var dialogs = new QueuedDialogs(
            selections:
            [
                "GPT-5.5 (gpt-5.5) recommended",
                "High"
            ],
            inputs: []);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            dialogs,
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        dialogs.SelectionCalls.Should().Be(2);
        selection.Current.Should().NotBeNull();
        selection.Current!.Chat?.Reasoning?.Effort.Should().Be(ReasoningEffort.High);
    }

    [Fact]
    public async Task ModelCommand_CancelingConfigurationDoesNotCommitSelection()
    {
        var selection = new AgentTuiModelSelectionState();
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "openai",
                    "OpenAI",
                    IsRegistered: true,
                    IsAuthenticated: true)
            ],
            [
                new AgentTuiModelChoice(
                    "openai",
                    "gpt-5.5",
                    "GPT-5.5",
                    IsRecommended: true,
                    Capabilities: new AgentTuiModelCapabilities(
                        SupportsTools: true,
                        SupportsReasoning: true))
            ]);
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection, configure: options =>
            {
                options.ConfigureSelection = (context, model) =>
                    AgentTuiModelConfigFlow.ConfigureAsync(context, model);
            })
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            new CancelSecondSelectionDialogs(),
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        selection.Current.Should().BeNull();
    }


    [Fact]
    public async Task ModelCommand_CanConfigureMoreGenericChatOptions()
    {
        var selection = new AgentTuiModelSelectionState();
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "openai",
                    "OpenAI",
                    IsRegistered: true,
                    IsAuthenticated: true)
            ],
            [
                new AgentTuiModelChoice(
                    "openai",
                    "gpt-5.5",
                    "GPT-5.5",
                    IsRecommended: true,
                    Capabilities: new AgentTuiModelCapabilities(
                        SupportsTools: true,
                        SupportsReasoning: true,
                        SupportsTemperature: true,
                        OutputTokenLimit: 4096))
            ]);
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection, configure: options =>
            {
                options.ConfigureSelection = (context, model) =>
                    AgentTuiModelConfigFlow.ConfigureAsync(context, model);
            })
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            new QueuedDialogs(
                selections:
                [
                    "More config",
                    "Sampling",
                    "Output length",
                    "Continue"
                ],
                inputs:
                [
                    "0.2",
                    "2048"
                ]),
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        selection.Current.Should().NotBeNull();
        selection.Current!.Chat.Should().NotBeNull();
        selection.Current.Chat!.Temperature.Should().Be(0.2);
        selection.Current.Chat.TopP.Should().BeNull();
        selection.Current.Chat.TopK.Should().BeNull();
        selection.Current.Chat.MaxOutputTokens.Should().Be(2048);
    }

    [Fact]
    public async Task ModelCommand_CanRunProviderModelConfigContributor()
    {
        var selection = new AgentTuiModelSelectionState();
        var contributor = new TestModelConfigContributor();
        var catalog = new TestModelCatalog(
            [
                new AgentTuiProviderChoice(
                    "test-provider",
                    "Test Provider",
                    IsRegistered: true,
                    IsAuthenticated: true)
            ],
            [
                new AgentTuiModelChoice(
                    "test-provider",
                    "test-model",
                    "Test Model",
                    IsRecommended: true,
                    Capabilities: new AgentTuiModelCapabilities(
                        SupportsTools: true,
                        SupportsTemperature: true))
            ]);
        var registry = new HpdAgentTuiBuilder()
            .AddModelSelection(catalog, selection, configure: options =>
            {
                options.ConfigureSelection = (context, model) =>
                    AgentTuiModelConfigFlow.ConfigureAsync(context, model, [contributor]);
            })
            .Build();
        registry.TryFindSlashCommand("/model", out var command, out var arguments).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);

        await command.ExecuteAsync(new AgentTuiCommandContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            new QueuedDialogs(
                selections:
                [
                    "More config",
                    "Provider behavior",
                    "Continue"
                ],
                inputs: []),
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        contributor.WasCalled.Should().BeTrue();
        selection.Current.Should().NotBeNull();
        selection.Current!.Chat?.ProviderOptions.Should().BeOfType<TestChatRequestOptions>()
            .Which.ProviderMode.Should().Be("strict");
    }

    private sealed class TestModelCatalog : IAgentTuiModelCatalog
    {
        private readonly IReadOnlyList<AgentTuiProviderChoice> _providers;
        private readonly IReadOnlyList<AgentTuiModelChoice> _models;

        public TestModelCatalog(
            IReadOnlyList<AgentTuiProviderChoice> providers,
            IReadOnlyList<AgentTuiModelChoice> models)
        {
            _providers = providers;
            _models = models;
        }

        public List<AgentTuiModelQuery> ModelQueries { get; } = [];

        public ValueTask<IReadOnlyList<AgentTuiProviderChoice>> GetProvidersAsync(
            AgentTuiModelCatalogContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_providers);

        public ValueTask<IReadOnlyList<AgentTuiModelChoice>> GetModelsAsync(
            AgentTuiModelCatalogContext context,
            string providerKey,
            AgentTuiModelQuery query,
            CancellationToken cancellationToken = default)
        {
            ModelQueries.Add(query);
            return ValueTask.FromResult<IReadOnlyList<AgentTuiModelChoice>>(
                _models
                    .Where(model => model.ProviderKey == providerKey)
                    .Where(model => !query.FreeOnly || model.IsFree)
                    .Where(model => string.IsNullOrWhiteSpace(query.Search)
                        || model.ModelId.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                        || model.DisplayName?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) == true)
                    .ToArray());
        }
    }

    private sealed class FirstChoiceDialogs : HPD.Agent.TUI.Composition.IAgentTuiDialogService
    {
        public bool HasOpenDialog => false;

        public Task<AgentTuiDialogResult<TResult>> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<TResult>.Dismissed());

        public bool Close(string key) => false;

        public bool CloseTop() => false;

        public Task<AgentTuiDialogResult<bool>> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue is null
                ? AgentTuiDialogResult<bool>.Dismissed()
                : AgentTuiDialogResult<bool>.Submitted(defaultValue.Value));

        public Task<AgentTuiDialogResult<T>> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
            => Task.FromResult(options.Count > 0
                ? AgentTuiDialogResult<T>.Submitted(options[0])
                : AgentTuiDialogResult<T>.Dismissed());

        public Task<AgentTuiDialogResult<string>> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<string>.Submitted(defaultValue ?? "manual-model"));

        public Task<AgentTuiDialogResult<string>> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<string>.Dismissed());
    }

    private sealed class PreferredChoiceDialogs(params string[] preferredLabels) : HPD.Agent.TUI.Composition.IAgentTuiDialogService
    {
        public bool HasOpenDialog => false;

        public Task<AgentTuiDialogResult<TResult>> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<TResult>.Dismissed());

        public bool Close(string key) => false;

        public bool CloseTop() => false;

        public Task<AgentTuiDialogResult<bool>> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue is null
                ? AgentTuiDialogResult<bool>.Dismissed()
                : AgentTuiDialogResult<bool>.Submitted(defaultValue.Value));

        public Task<AgentTuiDialogResult<T>> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
        {
            foreach (var preferred in preferredLabels)
            {
                var match = options.FirstOrDefault(option =>
                    string.Equals(titleSelector(option), preferred, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    return Task.FromResult(AgentTuiDialogResult<T>.Submitted(match));
            }

            return Task.FromResult(options.Count > 0
                ? AgentTuiDialogResult<T>.Submitted(options[0])
                : AgentTuiDialogResult<T>.Dismissed());
        }

        public Task<AgentTuiDialogResult<string>> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<string>.Submitted(defaultValue ?? "manual-model"));

        public Task<AgentTuiDialogResult<string>> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<string>.Dismissed());
    }

    private sealed class CancelSecondSelectionDialogs : HPD.Agent.TUI.Composition.IAgentTuiDialogService
    {
        private int _selections;

        public bool HasOpenDialog => false;

        public Task<AgentTuiDialogResult<TResult>> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<TResult>.Dismissed());

        public bool Close(string key) => false;

        public bool CloseTop() => false;

        public Task<AgentTuiDialogResult<bool>> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue is null
                ? AgentTuiDialogResult<bool>.Dismissed()
                : AgentTuiDialogResult<bool>.Submitted(defaultValue.Value));

        public Task<AgentTuiDialogResult<T>> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
        {
            _selections++;
            if (_selections >= 2)
                return Task.FromResult(AgentTuiDialogResult<T>.Dismissed());

            return Task.FromResult(options.Count > 0
                ? AgentTuiDialogResult<T>.Submitted(options[0])
                : AgentTuiDialogResult<T>.Dismissed());
        }

        public Task<AgentTuiDialogResult<string>> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<string>.Submitted(defaultValue ?? "manual-model"));

        public Task<AgentTuiDialogResult<string>> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<string>.Dismissed());
    }

    private sealed class QueuedDialogs(
        IReadOnlyList<string?> selections,
        IReadOnlyList<string?> inputs) : HPD.Agent.TUI.Composition.IAgentTuiDialogService
    {
        private int _selectionIndex;
        private int _inputIndex;

        public bool HasOpenDialog => false;

        public int SelectionCalls { get; private set; }

        public int FilteredSelectionCalls { get; private set; }

        public int InputCalls { get; private set; }

        public Task<AgentTuiDialogResult<TResult>> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<TResult>.Dismissed());

        public bool Close(string key) => false;

        public bool CloseTop() => false;

        public Task<AgentTuiDialogResult<bool>> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue is null
                ? AgentTuiDialogResult<bool>.Dismissed()
                : AgentTuiDialogResult<bool>.Submitted(defaultValue.Value));

        public Task<AgentTuiDialogResult<T>> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
        {
            SelectionCalls++;
            if (_selectionIndex < selections.Count)
            {
                var preferred = selections[_selectionIndex];
                if (preferred is null)
                {
                    _selectionIndex++;
                    return Task.FromResult(AgentTuiDialogResult<T>.Dismissed());
                }

                var match = options.FirstOrDefault(option =>
                    string.Equals(titleSelector(option), preferred, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    _selectionIndex++;
                    return Task.FromResult(AgentTuiDialogResult<T>.Submitted(match));
                }
            }

            return Task.FromResult(options.Count > 0
                ? AgentTuiDialogResult<T>.Submitted(options[0])
                : AgentTuiDialogResult<T>.Dismissed());
        }

        public Task<AgentTuiDialogResult<T>> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            AgentTuiSelectOptions selectOptions,
            CancellationToken cancellationToken = default)
        {
            if (selectOptions.AllowFilter)
            {
                FilteredSelectionCalls++;
            }

            return SelectAsync(title, options, titleSelector, cancellationToken);
        }

        public Task<AgentTuiDialogResult<string>> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
        {
            InputCalls++;
            var value = _inputIndex < inputs.Count
                ? inputs[_inputIndex++]
                : defaultValue;

            return Task.FromResult(value is null
                ? AgentTuiDialogResult<string>.Dismissed()
                : AgentTuiDialogResult<string>.Submitted(value));
        }

        public Task<AgentTuiDialogResult<string>> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<string>.Dismissed());
    }

    private sealed class TestModelConfigContributor : IAgentTuiModelConfigContributor
    {
        public bool WasCalled { get; private set; }

        public string Label => "Provider behavior";

        public bool CanConfigure(AgentTuiSelectedModel model)
            => model.ProviderKey == "test-provider";

        public ValueTask<AgentTuiSelectedModel?> ConfigureAsync(
            AgentTuiCommandContext context,
            AgentTuiSelectedModel model,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            var chat = new ChatClientConfig
            {
                ProviderOptions = new TestChatRequestOptions("strict")
            };
            return ValueTask.FromResult<AgentTuiSelectedModel?>(model with { Chat = chat });
        }
    }

    private sealed record TestChatRequestOptions(string ProviderMode) : IChatRequestOptions
    {
        public void ApplyTo(ChatOptions options) { }
    }

    private sealed class NoopRuntime : IHpdAgentTuiRuntime
    {
        public Task<AgentTuiScopeResolution> ResolveInitialScopeAsync(
            AgentTuiRuntimeScope? requested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiScopeResolution(
                requested ?? new AgentTuiRuntimeScope("agent", "session", "main"),
                IsDurable: true));

        public Task<AgentTuiRuntimeScope> EnsureDurableScopeAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(scope);

        public async IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
            AgentTuiRuntimeScope scope,
            ThreadJournalCursor after,
            ThreadJournalCursor initialObservedCursor,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<AgentTuiSubmitResult> SubmitInputAsync(
            AgentTuiRuntimeScope scope,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Submitted(scope));

        public Task<AgentRespondResult> AnswerRequestAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentRespondResult(AgentRespondStatus.Accepted, ((IAgentResponseEvent)response).RequestId));

        public Task<AgentTuiThreadState> GetThreadStateAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiThreadState(ThreadJournalCursor.Start(1), null, []));

        private static AgentTuiSubmitResult Submitted(AgentTuiRuntimeScope scope) => new(
            AgentInputDisposition.Queued,
            "run",
            new AgentTuiThreadExecution("run", scope.AgentId, scope.SessionId, scope.ThreadId, "active", DateTimeOffset.UtcNow));
    }
}
