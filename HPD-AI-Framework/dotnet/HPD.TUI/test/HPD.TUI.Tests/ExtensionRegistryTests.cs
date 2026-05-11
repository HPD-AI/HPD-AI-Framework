using HPD.TUI.Content;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Extensions;
using HPD.TUI.Models;
using HPD.TUI.Views;

namespace HPD.TUI.Tests;

public sealed class ExtensionRegistryTests
{
    [Fact]
    public void Registry_RegistersCommandDescriptors()
    {
        var executed = false;
        var registry = new TuiExtensionRegistry();

        registry.RegisterCommand(new CommandDescriptor("hello", _ => executed = true));
        var router = new CommandRouter(registry.Commands);
        var handled = router.TryExecute("/hello".AsSpan());

        Assert.True(handled);
        Assert.True(executed);
    }

    [Fact]
    public void Registry_RegistersAutocompleteProviders()
    {
        var registry = new TuiExtensionRegistry();
        var provider = new StaticAutocompleteProvider();

        registry.RegisterAutocompleteProvider(provider);

        Assert.Same(provider, Assert.Single(registry.AutocompleteProviders));
    }

    [Fact]
    public void Registry_RendersSemanticContentWithRegisteredRenderer()
    {
        var registry = new TuiExtensionRegistry()
            .RegisterContentRenderer(new TestTextBlockRenderer());
        var block = TextBlock.Create("hello");

        var handled = registry.TryRenderContent(block, out var component);

        Assert.True(handled);
        var text = Assert.IsType<TextBlock>(component);
        Assert.Equal("rendered:hello", text.Text);
    }

    [Fact]
    public void Registry_ProvidesTypedSelections()
    {
        var registry = new TuiExtensionRegistry()
            .RegisterSelectionProvider(new NumberSelectionProvider());

        var found = registry.TryGetSelection<int>(out var selection);

        Assert.True(found);
        Assert.Equal(2, selection.Items.Count);
    }

    [Fact]
    public void Registry_ChoosesFirstMatchingViewStrategy()
    {
        var registry = new TuiExtensionRegistry()
            .RegisterViewStrategy(new NarrowStringStrategy());
        var context = new ViewStrategyContext(10, 4);

        var handled = registry.TryCreateView("abc", in context, out var component);

        Assert.True(handled);
        var text = Assert.IsType<TextBlock>(component);
        Assert.Equal("narrow:abc", text.Text);
    }

    [Fact]
    public void Registry_MapsToolResultsToSemanticBlocks()
    {
        var registry = new TuiExtensionRegistry()
            .RegisterToolResultMapper(new MarkdownToolResultMapper());

        var handled = registry.TryMapToolResult("text/markdown", "# Title".AsMemory(), out var block);

        Assert.True(handled);
        Assert.IsType<MarkdownBlock>(block);
    }

    [Fact]
    public void ExtensionContext_ExposesSemanticRegistry()
    {
        var manager = new ExtensionManager();

        manager.Load(new SemanticExtension());

        Assert.Single(manager.Registry.Commands.Commands);
    }

    private sealed class StaticAutocompleteProvider : IAutocompleteProvider
    {
        public bool CanProvide(AutocompleteTrigger trigger) => trigger.Marker == '/';

        public IEnumerable<AutocompleteSuggestion> GetSuggestions(AutocompleteTrigger trigger)
        {
            yield return new AutocompleteSuggestion("help", "/help");
        }
    }

    private sealed class TestTextBlockRenderer : IContentRenderer<TextBlock>
    {
        public IComponent Render(TextBlock block) => TextBlock.Create("rendered:" + block.Text);
    }

    private sealed class NumberSelectionProvider : ISelectionProvider<int>
    {
        public SelectionModel<int> GetSelection()
        {
            return new SelectionModel<int>()
                .Add(1, "one")
                .Add(2, "two");
        }
    }

    private sealed class NarrowStringStrategy : IViewStrategy<string>
    {
        public bool CanRender(string model, in ViewStrategyContext context) => context.Width <= 20;

        public IComponent CreateView(string model, in ViewStrategyContext context) => TextBlock.Create("narrow:" + model);
    }

    private sealed class MarkdownToolResultMapper : IToolResultMapper
    {
        public bool TryMap(string contentType, ReadOnlyMemory<char> payload, out IContentBlock block)
        {
            if (contentType == "text/markdown")
            {
                block = MarkdownBlock.Create(payload.ToString());
                return true;
            }

            block = null!;
            return false;
        }
    }

    private sealed class SemanticExtension : IExtension
    {
        public string Name => "semantic";

        public Version Version { get; } = new(1, 0, 0);

        public void Initialize(ExtensionContext context)
        {
            context.Registry.RegisterCommand(new CommandDescriptor("semantic", _ => { }));
        }
    }
}
