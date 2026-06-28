using FluentAssertions;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;
using HPD.TUI.Controllers;

namespace HPD.Agent.TUI.Tests;

public sealed class SlashCommandTests
{
    [Fact]
    public void Build_IncludesDefaultSlashCommands()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .Build();

        registry.Commands.Select(command => command.SlashName)
            .Should()
            .Contain(["help", "clear"]);
    }

    [Fact]
    public void Build_OrdersSlashCommandsByDescriptorOrder()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .AddSlashCommand(new HpdAgentTuiCommandDescriptor("first", _ => { })
            {
                Order = 100
            })
            .AddSlashCommand(new HpdAgentTuiCommandDescriptor("middle", _ => { }))
            .Build();

        registry.Commands.Select(static command => command.SlashName)
            .Should()
            .ContainInOrder("first", "middle", "help", "clear");
    }

    [Fact]
    public void AddSlashCommand_FailsOnDuplicate()
    {
        var builder = TuiTestBuilder.Create()
            .AddSlashCommand(new HpdAgentTuiCommandDescriptor("sample", _ => { }));

        var act = () => builder.AddSlashCommand(new HpdAgentTuiCommandDescriptor("sample", _ => { }));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryFindSlashCommand_ParsesArguments()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .Build();

        var found = registry.TryFindSlashCommand("/help verbose", out var command, out var arguments);

        found.Should().BeTrue();
        command.SlashName.Should().Be("help");
        arguments.Should().Be("verbose");
    }

    [Fact]
    public void TryFindSlashCommand_TreatsBareSlashAsHelp()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .Build();

        var found = registry.TryFindSlashCommand("/", out var command, out var arguments);

        found.Should().BeTrue();
        command.SlashName.Should().Be("help");
        arguments.Should().BeEmpty();
    }

    [Fact]
    public async Task SlashCommandAgentAutocompleteProvider_ReturnsMatchingCommands()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .Build();
        var provider = new SlashCommandAgentAutocompleteProvider(registry);

        var context = new HPD.Agent.TUI.Composition.AgentTuiAutocompleteContext(
            CreateRequest("/he"),
            scope: null,
            shell: null);
        var suggestions = new CapturingAutocompleteSink();
        await provider.GetSuggestionsAsync(context, suggestions);

        suggestions.Items.Should().ContainSingle();
        suggestions.Items[0].Title.Should().Be("/help");
        suggestions.Items[0].InsertText.Should().Be("/help");
        suggestions.Items[0].SubmitOnAccept.Should().BeTrue();
    }

    [Fact]
    public async Task SlashCommandAgentAutocompleteProvider_DoesNotSuggestExactCommand()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .Build();
        var provider = new SlashCommandAgentAutocompleteProvider(registry);

        var context = new HPD.Agent.TUI.Composition.AgentTuiAutocompleteContext(
            CreateRequest("/help"),
            scope: null,
            shell: null);
        var suggestions = new CapturingAutocompleteSink();
        await provider.GetSuggestionsAsync(context, suggestions);

        suggestions.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultClearCommand_ClearsTranscript()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .Build();
        var shell = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"));
        shell.Transcript.AddFinal(new TranscriptEntry(
            Id: "row",
            EntryKey: null,
            new NoticeCell("test", new Text("hello")),
            new TranscriptEntryMetadata()));

        registry.TryFindSlashCommand("/clear", out var command, out var arguments).Should().BeTrue();
        await command.ExecuteAsync(new AgentTuiCommandContext(
            shell.Scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            NoopDialogs.Instance,
            TuiTestBuilder.NoopSessionUi,
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        shell.Transcript.Count.Should().Be(0);
    }

    [Fact]
    public async Task DefaultHelpCommand_IncludesCommandsAddedAfterDefaults()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .AddSlashCommand(new HpdAgentTuiCommandDescriptor("sessions", _ => { })
            {
                Description = "List sessions."
            })
            .Build();
        var shell = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"));

        registry.TryFindSlashCommand("/help", out var command, out var arguments).Should().BeTrue();
        await command.ExecuteAsync(new AgentTuiCommandContext(
            shell.Scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            NoopDialogs.Instance,
            TuiTestBuilder.NoopSessionUi,
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments));

        shell.Transcript.Count.Should().Be(0);
        shell.Navigation.ActivePageId.Should().Be("hpd.help");
        registry.TryFindPage("hpd.help", out var page).Should().BeTrue();
        page.Render(new AgentTuiPageContext(
                shell.Scope,
                shell,
                shell.Navigation,
                registry,
                page,
                height: 10))
            .Should()
            .BeOfType<Markdown>()
            .Subject
            .Source
            .Should()
            .Contain("`/sessions` List sessions.");
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

        public Task InterruptAsync(
            AgentTuiRuntimeScope scope,
            string reason,
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

    private sealed class NoopDialogs : HPD.Agent.TUI.Composition.IAgentTuiDialogService
    {
        public static NoopDialogs Instance { get; } = new();

        public bool HasOpenDialog => false;

        public Task<TResult?> ShowAsync<TResult>(
            string key,
            Func<HPD.Agent.TUI.Composition.AgentTuiDialogContext<TResult>, HPD.TUI.Core.IComponent> componentFactory,
            CancellationToken cancellationToken = default)
            => Task.FromResult<TResult?>(default);

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
            => Task.FromResult(defaultValue);

        public Task<string?> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private static AutocompleteRequest CreateRequest(string text)
        => new(text, text.Length);

    private sealed class CapturingAutocompleteSink : IAutocompleteSuggestionSink
    {
        private readonly List<AutocompleteSuggestion> _items = [];

        public IReadOnlyList<AutocompleteSuggestion> Items => _items;

        public void Add(AutocompleteSuggestion suggestion, AutocompleteReplacement? replacement = null)
            => _items.Add(suggestion);
    }
}
