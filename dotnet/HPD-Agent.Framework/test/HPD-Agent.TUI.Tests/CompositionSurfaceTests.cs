using FluentAssertions;
using HPD.Agent;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.TUI.Components;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;
using HPD.TUI.Views;
using System.Text;

namespace HPD.Agent.TUI.Tests;

public sealed class CompositionSurfaceTests
{
    [Fact]
    public void AddStatusItem_FailsOnDuplicateKey()
    {
        var builder = TuiTestBuilder.Create()
            .AddStatusItem("sample.status", new TextStatusItem("one"));

        var act = () => builder.AddStatusItem("sample.status", new TextStatusItem("two"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryAddStatusItem_KeepsExistingContribution()
    {
        var registry = TuiTestBuilder.Create()
            .AddStatusItem("sample.status", new TextStatusItem("one"))
            .TryAddStatusItem("sample.status", new TextStatusItem("two"))
            .Build();

        registry.StatusItems.Should().ContainSingle();
        registry.StatusItems[0].Value.Should().BeOfType<TextStatusItem>()
            .Which.Text.Should().Be("one");
    }

    [Fact]
    public void ReplaceStatusItem_ReplacesExistingContribution()
    {
        var registry = TuiTestBuilder.Create()
            .AddStatusItem("sample.status", new TextStatusItem("one"))
            .ReplaceStatusItem("sample.status", new TextStatusItem("two"))
            .Build();

        registry.StatusItems.Should().ContainSingle();
        registry.StatusItems[0].Value.Should().BeOfType<TextStatusItem>()
            .Which.Text.Should().Be("two");
    }

    [Fact]
    public void ReplaceStatusItem_FailsWhenMissing()
    {
        var builder = TuiTestBuilder.Create();

        var act = () => builder.ReplaceStatusItem("missing", new TextStatusItem("value"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddWidget_FailsOnDuplicateSlotAndKey()
    {
        var builder = TuiTestBuilder.Create()
            .AddWidget(TuiSlot.AboveEditor, "sample.widget", new TextWidget("one"));

        var act = () => builder.AddWidget(TuiSlot.AboveEditor, "sample.widget", new TextWidget("two"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SameWidgetKey_CanAppearInDifferentSlots()
    {
        var registry = TuiTestBuilder.Create()
            .AddWidget(TuiSlot.AboveEditor, "sample.widget", new TextWidget("above"))
            .AddWidget(TuiSlot.BelowEditor, "sample.widget", new TextWidget("below"))
            .Build();

        registry.AboveEditorWidgets.Should().ContainSingle();
        registry.BelowEditorWidgets.Should().ContainSingle();
    }

    [Fact]
    public void ReplaceHeader_RequiresExistingHeader()
    {
        var builder = TuiTestBuilder.Create();

        var act = () => builder.ReplaceHeader(_ => new Text("custom"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddAgentTuiDefaults_DoesNotOverrideExistingHeader()
    {
        var registry = TuiTestBuilder.Create()
            .AddHeader(_ => new Text("custom header"))
            .AddAgentTuiDefaults()
            .Build();
        var shell = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"))
        {
            HeaderText = "default header"
        };

        var rendered = TuiCapture.RenderToString(
            new ShellContributionView(shell, registry.Header),
            width: 80,
            height: 2,
            trimTrailingBlankLines: true);

        rendered.Should().Contain("custom header");
    }

    [Fact]
    public void DecorateHeader_WrapsExistingHeader()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .DecorateHeader(inner => new DelegateAgentTuiShellComponent(context =>
                new Stack()
                    .Add(inner.Create(context))
                    .Add(new Text("decorated header"))))
            .Build();
        var shell = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"))
        {
            HeaderText = "default header"
        };

        var rendered = TuiCapture.RenderToString(
            new ShellContributionView(shell, registry.Header),
            width: 80,
            height: 4,
            trimTrailingBlankLines: true);

        rendered.Should().Contain("default header");
        rendered.Should().Contain("decorated header");
    }

    [Fact]
    public void DecorateFooter_RequiresExistingFooter()
    {
        var builder = TuiTestBuilder.Create();

        var act = () => builder.DecorateFooter(inner => inner);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DefaultShellLayout_RendersRegisteredContributions()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .ReplaceHeader(_ => new Text("custom header"))
            .ReplaceFooter(_ => new Text("custom footer"))
            .AddStatusItem("sample.status", new TextStatusItem("status contribution"))
            .AddWidget(TuiSlot.AboveEditor, "sample.above", new TextWidget("above contribution"))
            .AddWidget(TuiSlot.BelowEditor, "sample.below", new TextWidget("below contribution"))
            .Build();
        var model = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"));
        model.Transcript.AddFinal(new TranscriptEntry(
            Id: "row",
            EntryKey: null,
            new AssistantMessageCell("assistant", new Markdown("hello")),
            new TranscriptEntryMetadata()));

        var view = registry.ShellLayout.Create(new AgentTuiShellLayoutContext(
            model,
            PromptView.Create("Ask HPD..."),
            registry,
            registry.ShellChrome));

        var rendered = TuiCapture.RenderToString(view, width: 96, height: 32, trimTrailingBlankLines: true);

        rendered.Should().Contain("custom header");
        rendered.Should().Contain("custom footer");
        rendered.Should().Contain("status contribution");
        rendered.Should().Contain("above contribution");
        rendered.Should().Contain("below contribution");
    }

    [Fact]
    public void AddAgentTuiDefaults_InstallsDefaultShellLayout()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .Build();

        registry.ShellLayout.Should().BeOfType<DefaultAgentTuiShellLayout>();
    }

    [Fact]
    public void AddAgentTuiDefaults_InstallsDefaultTranscriptRenderers()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .Build();

        registry.TranscriptRenderers.TryFindRenderer<RunStatusCell>(
                AgentTuiTranscriptRendererKeys.RunStatus,
                out var renderer)
            .Should().BeTrue();
        renderer.Should().NotBeNull();
    }

    [Fact]
    public void AddTranscriptRenderer_FailsOnDuplicateKey()
    {
        var builder = TuiTestBuilder.Create()
            .AddTranscriptRenderer("sample.transcript", new TextTranscriptRenderer<RunStatusCell>("one"));

        var act = () => builder.AddTranscriptRenderer(
            "sample.transcript",
            new TextTranscriptRenderer<NoticeCell>("two"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddTranscriptRenderer_FailsOnDuplicateCellType()
    {
        var builder = TuiTestBuilder.Create()
            .AddTranscriptRenderer("sample.one", new TextTranscriptRenderer<RunStatusCell>("one"));

        var act = () => builder.AddTranscriptRenderer(
            "sample.two",
            new TextTranscriptRenderer<RunStatusCell>("two"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryAddTranscriptRenderer_KeepsExistingRenderer()
    {
        var first = new TextTranscriptRenderer<RunStatusCell>("one");
        var second = new TextTranscriptRenderer<RunStatusCell>("two");

        var registry = TuiTestBuilder.Create()
            .AddTranscriptRenderer("sample.run", first)
            .TryAddTranscriptRenderer("sample.run", second)
            .Build();

        registry.TranscriptRenderers.TryFindRenderer<RunStatusCell>(
                "sample.run",
                out var renderer)
            .Should().BeTrue();
        renderer.Should().BeSameAs(first);
    }

    [Fact]
    public void ReplaceTranscriptRenderer_ReplacesExistingRenderer()
    {
        var replacement = new TextTranscriptRenderer<RunStatusCell>("two");

        var registry = TuiTestBuilder.Create()
            .AddTranscriptRenderer("sample.run", new TextTranscriptRenderer<RunStatusCell>("one"))
            .ReplaceTranscriptRenderer("sample.run", replacement)
            .Build();

        registry.TranscriptRenderers.TryFindRenderer<RunStatusCell>(
                "sample.run",
                out var renderer)
            .Should().BeTrue();
        renderer.Should().BeSameAs(replacement);
    }

    [Fact]
    public void ReplaceTranscriptRenderer_RequiresExistingRenderer()
    {
        var builder = TuiTestBuilder.Create();

        var act = () => builder.ReplaceTranscriptRenderer(
            "missing",
            new TextTranscriptRenderer<RunStatusCell>("value"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DecorateTranscriptRenderer_WrapsExistingRenderer()
    {
        var registry = TuiTestBuilder.Create()
            .AddTranscriptRenderer("sample.run", new TextTranscriptRenderer<RunStatusCell>("inner"))
            .DecorateTranscriptRenderer<RunStatusCell>(
                "sample.run",
                inner => new DelegateAgentTuiTranscriptRenderer<RunStatusCell>(context =>
                    new Stack()
                        .Add(inner.Create(context))
                        .Add(new Text("outer"))))
            .Build();
        var entry = new TranscriptEntry(
            Id: "run",
            EntryKey: null,
            Cell: new RunStatusCell("run-123", TranscriptRunState.Completed),
            Metadata: new TranscriptEntryMetadata());

        var rendered = TuiCapture.RenderToString(
            registry.TranscriptRenderers.Create(entry),
            width: 80,
            height: 4,
            trimTrailingBlankLines: true);

        rendered.Should().Contain("inner");
        rendered.Should().Contain("outer");
    }

    [Fact]
    public void TryAddShellLayout_KeepsExistingLayout()
    {
        var first = new TestShellLayout("first");
        var second = new TestShellLayout("second");

        var registry = TuiTestBuilder.Create()
            .AddShellLayout(first)
            .TryAddShellLayout(second)
            .Build();

        registry.ShellLayout.Should().BeSameAs(first);
    }

    [Fact]
    public void ReplaceShellLayout_ReplacesExistingLayout()
    {
        var replacement = new TestShellLayout("replacement");

        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .ReplaceShellLayout(replacement)
            .Build();

        registry.ShellLayout.Should().BeSameAs(replacement);
    }

    [Fact]
    public void ReplaceShellLayout_RequiresExistingLayout()
    {
        var builder = TuiTestBuilder.Create();

        var act = () => builder.ReplaceShellLayout(new TestShellLayout("replacement"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ConfigureShellChrome_IsFrozenInRegistry()
    {
        var builder = TuiTestBuilder.Create()
            .ConfigureShellChrome(chrome =>
            {
                chrome.ShowSectionTitles = false;
                chrome.Prompt = ShellSectionChrome.Frame(null, BorderSpec.Ascii);
            });

        var registry = builder.Build();
        builder.ConfigureShellChrome(chrome =>
        {
            chrome.ShowSectionTitles = true;
            chrome.Prompt = ShellSectionChrome.Frame("Changed", BorderSpec.Rounded);
        });

        registry.ShellChrome.ShowSectionTitles.Should().BeFalse();
        registry.ShellChrome.Prompt.Title.Should().BeNull();
        registry.ShellChrome.Prompt.Border.Should().Be(BorderSpec.Ascii);
    }

    [Fact]
    public void AddRunConfigContributor_StoresContributorInRegistry()
    {
        var contributor = new DelegateRunConfigContributor();

        var registry = TuiTestBuilder.Create()
            .AddRunConfigContributor("sample.run-config", contributor)
            .Build();

        registry.RunConfigContributors.Should().ContainSingle(contribution =>
            contribution.Key == "sample.run-config" &&
            ReferenceEquals(contribution.Value, contributor));
    }

    [Fact]
    public void AddAutocompleteProvider_AppendsProviderAfterSlashProvider()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .AddAutocompleteProvider("sample.hash", new HashAutocompleteProvider())
            .Build();

        registry.AutocompleteProviders.Select(provider => provider.Key)
            .Should()
            .Equal("hpd.slash-commands", "sample.hash");
    }

    [Fact]
    public async Task ReplaceAutocompleteProvider_ReplacesExistingProvider()
    {
        var registry = TuiTestBuilder.Create()
            .AddAutocompleteProvider("sample.hash", new HashAutocompleteProvider("#one"))
            .ReplaceAutocompleteProvider("sample.hash", new HashAutocompleteProvider("#two"))
            .Build();
        var provider = registry.AutocompleteProviders.Single(contribution => contribution.Key == "sample.hash").Value;

        var request = new AutocompleteRequest("#", 1);
        var suggestions = new CapturingAutocompleteSink();
        await provider.GetSuggestionsAsync(new AgentTuiAutocompleteContext(
            request,
            null,
            null),
            suggestions);

        suggestions.Items.Select(suggestion => suggestion.InsertText).Should().Equal("#two");
    }

    [Fact]
    public void ReplacePrompt_RequiresExistingPrompt()
    {
        var builder = TuiTestBuilder.Create();

        var act = () => builder.ReplacePrompt(new DefaultAgentTuiPromptFactory());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PromptFactory_CanReplaceDefaultPrompt()
    {
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .ReplacePrompt(new DefaultAgentTuiPromptFactory { Placeholder = "Custom prompt..." })
            .Build();
        var shell = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"));
        var prompt = registry.PromptFactory.Create(
            new AgentTuiPromptContext(shell.Scope, shell),
            _ => { },
            new AutocompleteController());

        var rendered = TuiCapture.RenderToString(prompt, width: 40, height: 2, trimTrailingBlankLines: true);

        rendered.Should().Contain("Custom prompt...");
    }

    [Fact]
    public void UseTheme_StoresThemeOnRegistry()
    {
        var theme = new Theme
        {
            Accent = new Style(new Color(1, 2, 3), Color.Default)
        };

        var registry = TuiTestBuilder.Create()
            .UseTheme(theme)
            .Build();

        registry.Theme.Should().Be(theme);
    }

    [Fact]
    public void AddShortcut_FailsOnDuplicateKey()
    {
        var first = new HpdAgentTuiShortcutDescriptor(
            "sample.shortcut",
            new KeyGesture(KeyCode.Enter, KeyModifiers.Ctrl),
            _ => { });
        var second = new HpdAgentTuiShortcutDescriptor(
            "sample.shortcut",
            new KeyGesture(KeyCode.Tab, KeyModifiers.Ctrl),
            _ => { });
        var builder = TuiTestBuilder.Create().AddShortcut(first);

        var act = () => builder.AddShortcut(second);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddShortcut_FailsOnDuplicateGesture()
    {
        var gesture = new KeyGesture(KeyCode.Enter, KeyModifiers.Ctrl);
        var builder = TuiTestBuilder.Create()
            .AddShortcut(new HpdAgentTuiShortcutDescriptor("one", gesture, _ => { }));

        var act = () => builder.AddShortcut(new HpdAgentTuiShortcutDescriptor("two", gesture, _ => { }));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryFindShortcut_MatchesRegisteredGesture()
    {
        var executed = false;
        var registry = TuiTestBuilder.Create()
            .AddShortcut(new HpdAgentTuiShortcutDescriptor(
                "sample.shortcut",
                new KeyGesture(KeyCode.Enter, KeyModifiers.Ctrl),
                _ => executed = true))
            .Build();

        var found = registry.TryFindShortcut(
            new KeyEvent(KeyCode.Enter, Modifiers: KeyModifiers.Ctrl),
            out var shortcut);

        found.Should().BeTrue();
        shortcut.Key.Should().Be("sample.shortcut");
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);
        shortcut.Execute(new AgentTuiShortcutContext(
            scope,
            shell,
            shell.Navigation,
            shortcut));
        executed.Should().BeTrue();
    }

    [Fact]
    public void ReplaceShortcut_FailsWhenNewGestureAlreadyExists()
    {
        var builder = TuiTestBuilder.Create()
            .AddShortcut(new HpdAgentTuiShortcutDescriptor(
                "one",
                new KeyGesture(KeyCode.Character, KeyModifiers.Ctrl, new Rune('a')),
                _ => { }))
            .AddShortcut(new HpdAgentTuiShortcutDescriptor(
                "two",
                new KeyGesture(KeyCode.Character, KeyModifiers.Ctrl, new Rune('b')),
                _ => { }));

        var act = () => builder.ReplaceShortcut(new HpdAgentTuiShortcutDescriptor(
            "one",
            new KeyGesture(KeyCode.Character, KeyModifiers.Ctrl, new Rune('b')),
            _ => { }));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddEventHandler_FailsOnDuplicateKey()
    {
        var builder = TuiTestBuilder.Create()
            .AddEventHandler("sample.event", new CountingEventHandler());

        var act = () => builder.AddEventHandler("sample.event", new CountingEventHandler());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryAddEventHandler_KeepsExistingContribution()
    {
        var first = new CountingEventHandler();
        var second = new CountingEventHandler();

        var registry = TuiTestBuilder.Create()
            .AddEventHandler("sample.event", first)
            .TryAddEventHandler("sample.event", second)
            .Build();

        registry.EventHandlers.Should().ContainSingle();
        registry.EventHandlers[0].Value.Should().BeSameAs(first);
    }

    [Fact]
    public void ReplaceEventHandler_ReplacesExistingContribution()
    {
        var replacement = new CountingEventHandler();

        var registry = TuiTestBuilder.Create()
            .AddEventHandler("sample.event", new CountingEventHandler())
            .ReplaceEventHandler("sample.event", replacement)
            .Build();

        registry.EventHandlers.Should().ContainSingle();
        registry.EventHandlers[0].Value.Should().BeSameAs(replacement);
    }

    [Fact]
    public void FindEventHandlers_ReturnsAllMatchingHandlersInOrder()
    {
        var first = new CountingEventHandler();
        var second = new CountingEventHandler();
        var registry = TuiTestBuilder.Create()
            .AddEventHandler("sample.first", first)
            .AddEventHandler("sample.second", second)
            .Build();

        var handlers = registry.FindEventHandlers(new TextDeltaEvent("hello", "m1")).ToArray();

        handlers.Select(handler => handler.Key).Should().Equal("sample.first", "sample.second");
    }

    private sealed class TextStatusItem : IAgentTuiStatusItem
    {
        public TextStatusItem(string text)
        {
            Text = text;
        }

        public string Text { get; }

        public IComponent Create(AgentTuiStatusContext context) => new Text(Text);
    }

    private sealed class TextWidget : IAgentTuiWidget
    {
        private readonly string _text;

        public TextWidget(string text)
        {
            _text = text;
        }

        public IComponent Create(AgentTuiWidgetContext context) => new Text(_text);
    }

    private sealed class HashAutocompleteProvider : IAgentTuiAutocompleteProvider
    {
        private readonly string _insertText;

        public HashAutocompleteProvider(string insertText = "#sample")
        {
            _insertText = insertText;
        }

        public bool CanProvide(AgentTuiAutocompleteContext context) => context.Marker == '#';

        public ValueTask GetSuggestionsAsync(
            AgentTuiAutocompleteContext context,
            IAutocompleteSuggestionSink suggestions,
            CancellationToken cancellationToken = default)
        {
            suggestions.Add(new AutocompleteSuggestion(_insertText, _insertText));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingAutocompleteSink : IAutocompleteSuggestionSink
    {
        private readonly List<AutocompleteSuggestion> _items = [];

        public IReadOnlyList<AutocompleteSuggestion> Items => _items;

        public void Add(AutocompleteSuggestion suggestion, AutocompleteReplacement? replacement = null)
            => _items.Add(suggestion);
    }

    private sealed class TestShellLayout : IAgentTuiShellLayout
    {
        private readonly string _text;

        public TestShellLayout(string text)
        {
            _text = text;
        }

        public IComponent Create(AgentTuiShellLayoutContext context) => new Text(_text);
    }

    private sealed class TextTranscriptRenderer<TCell> : IAgentTuiTranscriptRenderer<TCell>
        where TCell : TranscriptCell
    {
        private readonly string _text;

        public TextTranscriptRenderer(string text)
        {
            _text = text;
        }

        public IComponent Create(AgentTuiTranscriptRenderContext<TCell> context) => new Text(_text);
    }

    private sealed class CountingEventHandler : AgentTuiEventHandler<TextDeltaEvent>
    {
        public int Count { get; private set; }

        public override ValueTask HandleAsync(
            TextDeltaEvent evt,
            AgentTuiEventContext context,
            CancellationToken cancellationToken)
        {
            Count++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DelegateRunConfigContributor : IAgentTuiRunConfigContributor
    {
        public void ConfigureRun(
            AgentTuiRunConfigContributionContext context,
            AgentRunConfigBuilder builder)
        {
            builder.SetProviderModel(context.Scope.AgentId, context.PromptText);
        }
    }
}
