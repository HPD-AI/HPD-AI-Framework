using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;
using HPD.TUI.Controllers;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Console.Demo;

internal static class SampleContributions
{
    public static HpdAgentTuiBuilder AddSampleContributions(this HpdAgentTuiBuilder tui)
        => tui.ReplaceHeader(context => new Text($"{context.Shell.HeaderText}   demo: composition surface"))
            .ReplaceFooter(context => new Text(context.Shell.FooterText))
            .AddFooterItem("sample.transcript-count", new TranscriptCountFooterItem())
            .AddWidget(TuiSlot.AboveEditor, "sample.hints", new TextWidget("try /sample, /status, or #sample autocomplete"))
            .AddWidget(TuiSlot.BelowEditor, "sample.shortcut", new TextWidget("shortcut: Ctrl+Enter appends a local TUI note"))
            .AddAutocompleteProvider("sample.hash", new SampleHashAutocompleteProvider())
            .AddSlashCommand(new HpdAgentTuiCommandDescriptor("sample", context =>
            {
                AppendTuiRow(context.Scope, context.Shell, "sample command executed");
            })
            {
                Title = "/sample",
                Description = "Append a demo shell row."
            })
            .AddShortcut(new HpdAgentTuiShortcutDescriptor(
                "sample.append-note",
                new KeyGesture(KeyCode.Enter, KeyModifiers.Ctrl),
                context => AppendTuiRow(context.Scope, context.Shell, "shortcut executed")));

    private static void AppendTuiRow(
        AgentTuiRuntimeScope scope,
        ChatShellModel shell,
        string text)
    {
        shell.Transcript.AddFinal(new TranscriptEntry(
            Id: $"sample-tui-{Guid.NewGuid():N}",
            EntryKey: null,
            Cell: new NoticeCell("demo", new Text(text)),
            Metadata: new TranscriptEntryMetadata(
                AgentId: scope.AgentId,
                AgentName: "tui",
                AgentChain: ["tui"])));
    }
}

internal sealed class TranscriptCountFooterItem : IAgentTuiFooterItem
{
    public IComponent Create(AgentTuiFooterContext context)
        => new Text($"transcript rows {context.Shell.Transcript.Count}");
}

internal sealed class TextWidget : IAgentTuiWidget
{
    private readonly string _text;

    public TextWidget(string text)
    {
        _text = text;
    }

    public IComponent Create(AgentTuiWidgetContext context) => new Text(_text);
}

internal sealed class SampleHashAutocompleteProvider : IAgentTuiAutocompleteProvider
{
    public bool CanProvide(AgentTuiAutocompleteContext context)
        => context.Marker == '#';

    public ValueTask GetSuggestionsAsync(
        AgentTuiAutocompleteContext context,
        IAutocompleteSuggestionSink suggestions,
        CancellationToken cancellationToken = default)
    {
        suggestions.Add(new AutocompleteSuggestion("#sample", "#sample"));
        suggestions.Add(new AutocompleteSuggestion("#workspace", "#workspace"));
        return ValueTask.CompletedTask;
    }
}
