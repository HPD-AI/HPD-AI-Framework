using FluentAssertions;
using HPD.Agent;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Core;

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
                    IsRecommended: true)
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
        runConfig!.ProviderKey.Should().Be("openrouter");
        runConfig.ModelId.Should().Be("deepseek/deepseek-chat");
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

        selection.Current.Should().BeEquivalentTo(new AgentTuiSelectedModel("openrouter", "model-a"));
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

        public ValueTask<IReadOnlyList<AgentTuiProviderChoice>> GetProvidersAsync(
            AgentTuiModelCatalogContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_providers);

        public ValueTask<IReadOnlyList<AgentTuiModelChoice>> GetModelsAsync(
            AgentTuiModelCatalogContext context,
            string providerKey,
            AgentTuiModelQuery query,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<AgentTuiModelChoice>>(
                _models.Where(model => model.ProviderKey == providerKey).ToArray());
    }

    private sealed class FirstChoiceDialogs : HPD.Agent.TUI.Composition.IAgentTuiDialogService
    {
        public bool HasOpenDialog => false;

        public void Show(string key, IComponent component)
        {
        }

        public bool Close(string key) => false;

        public bool CloseTop() => false;

        public Task<bool?> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(defaultValue);

        public Task<T?> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
            => Task.FromResult(options.Count > 0 ? options[0] : default);

        public Task<string?> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(defaultValue ?? "manual-model");
    }

    private sealed class NoopRuntime : IHpdAgentTuiRuntime
    {
        public Task<AgentTuiRuntimeScope> EnsureScopeAsync(
            AgentTuiRuntimeScope? requested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(requested ?? new AgentTuiRuntimeScope("agent", "session", "main"));

        public async IAsyncEnumerable<AgentEvent> ObserveAsync(
            AgentTuiRuntimeScope scope,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task SubmitInputAsync(
            AgentTuiRuntimeScope scope,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RespondAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AgentEvent>> GetThreadEventsAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentEvent>>([]);

        public Task<AgentTuiThreadRun?> GetActiveRunAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult<AgentTuiThreadRun?>(null);
    }
}
