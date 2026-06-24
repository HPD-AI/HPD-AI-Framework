using FluentAssertions;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;
using HPD.TUI.Rendering;
using HPD.TUI.Views;

namespace HPD.Agent.TUI.Tests;

public sealed class DefaultAgentTuiShellLayoutTests
{
    [Fact]
    public void Render_IncludesShellSlots()
    {
        var model = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"))
        {
            HeaderText = "test header",
            FooterText = "test footer"
        };
        model.Transcript.AddFinal(Row("row-1", "assistant", "hello"));
        model.AboveEditor.Add(new Text("above widget"));
        model.BelowEditor.Add(new Text("below widget"));

        var shell = CreateShell(model);

        var text = TuiCapture.RenderToString(shell, width: 96, height: 32, trimTrailingBlankLines: true);

        text.Should().Contain("test header");
        text.Should().Contain("above widget");
        text.Should().Contain("Ask HPD");
        text.Should().Contain("below widget");
        text.Should().Contain("test footer");
        text.Should().NotContain("Transcript");
        text.Should().NotContain("Status");
    }

    [Fact]
    public void Render_IncludesWidgetAddedAfterShellCreation()
    {
        var model = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"));
        var shell = CreateShell(model);

        model.AboveEditor.Add(new Text("late permission card"));

        var text = TuiCapture.RenderToString(shell, width: 96, height: 24, trimTrailingBlankLines: true);

        text.Should().Contain("late permission card");
    }

    [Fact]
    public void Render_GivesTranscriptMoreRowsWhenTerminalIsTaller()
    {
        var model = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"));
        for (var i = 1; i <= 18; i++)
        {
            model.Transcript.AddFinal(Row($"row-{i}", "assistant", $"message {i:00}"));
        }

        var shell = CreateShell(model);

        var shortText = TuiCapture.RenderToString(shell, width: 96, height: 18, trimTrailingBlankLines: true);
        var tallText = TuiCapture.RenderToString(shell, width: 96, height: 36, trimTrailingBlankLines: true);

        shortText.Should().NotContain("message 12");
        CountMessages(tallText).Should().BeGreaterThan(CountMessages(shortText));
        tallText.Should().Contain("message 18");
        tallText.Should().Contain("Ask HPD");
    }

    [Fact]
    public void Render_WithTallHeader_KeepsPromptInViewport()
    {
        var model = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"));
        for (var i = 1; i <= 20; i++)
        {
            model.Transcript.AddFinal(Row($"row-{i}", "assistant", $"message {i:00}"));
        }

        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .ReplaceHeader(_ => new Markdown("logo one\nlogo two\nlogo three\nlogo four\nlogo five\nlogo six"))
            .Build();
        var shell = CreateShell(model, registry);

        var lines = TuiCapture.RenderToLines(shell, width: 96, height: 18);

        lines.Should().Contain(line => line.Contains("Ask HPD", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_UsesConfiguredShellChrome()
    {
        var model = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"));
        model.Transcript.AddFinal(Row("row-1", "assistant", "hello"));
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .ConfigureShellChrome(chrome =>
            {
                chrome.ShowSectionTitles = false;
                chrome.Transcript = ShellSectionChrome.Bare();
                chrome.Prompt = ShellSectionChrome.Frame(null, HPD.TUI.Layout.BorderSpec.Ascii);
            })
            .Build();

        var shell = CreateShell(model, registry);

        var text = TuiCapture.RenderToString(shell, width: 96, height: 24, trimTrailingBlankLines: true);

        text.Should().NotContain("Transcript");
        text.Should().NotContain("Prompt");
        text.Should().Contain("+");
        text.Should().Contain("Ask HPD");
    }

    private static HPD.TUI.Core.IComponent CreateShell(
        ChatShellModel model,
        HpdAgentTuiRegistry? registry = null)
    {
        registry ??= new HpdAgentTuiBuilder().AddAgentTuiDefaults().Build();
        return registry.ShellLayout.Create(new AgentTuiShellLayoutContext(
            model,
            PromptView.Create("Ask HPD..."),
            registry,
            registry.ShellChrome));
    }

    private static TranscriptEntry Row(string id, string label, string text)
        => new(
            id,
            EntryKey: null,
            new AssistantMessageCell(label, new Markdown(text)),
            new TranscriptEntryMetadata());

    private static int CountMessages(string text)
        => Enumerable.Range(1, 18).Count(i => text.Contains($"message {i:00}", StringComparison.Ordinal));
}
