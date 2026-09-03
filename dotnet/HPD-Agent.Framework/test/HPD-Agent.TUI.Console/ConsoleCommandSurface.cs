using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Console;

internal static class ConsoleCommandSurface
{
    public const string PageId = "console.command-surface";

    private static readonly object Gate = new();
    private static string _title = "Command";
    private static string _markdown = "No command output.";
    private static TranscriptSeverity _severity = TranscriptSeverity.Info;

    public static HpdAgentTuiBuilder AddConsoleCommandSurface(this HpdAgentTuiBuilder tui)
        => tui.TryAddPage(new HpdAgentTuiPageDescriptor(PageId, Render)
        {
            Title = "Command Surface",
            Description = "Current console command output.",
            Hidden = true
        });

    public static void Show(
        AgentTuiCommandContext context,
        string title,
        string markdown,
        TranscriptSeverity severity = TranscriptSeverity.Info)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        lock (Gate)
        {
            _title = title;
            _markdown = markdown ?? "";
            _severity = severity;
        }

        context.Navigation.GoToPage(PageId);
    }

    private static IComponent Render(AgentTuiPageContext context)
    {
        string title;
        string markdown;
        TranscriptSeverity severity;
        lock (Gate)
        {
            title = _title;
            markdown = _markdown;
            severity = _severity;
        }

        var prefix = severity switch
        {
            TranscriptSeverity.Warning => "**Warning**",
            TranscriptSeverity.Error => "**Error**",
            _ => null
        };

        var body = string.IsNullOrWhiteSpace(prefix)
            ? $"**{EscapeMarkdown(title)}**\n\n{markdown}"
            : $"**{EscapeMarkdown(title)}**\n\n{prefix}\n\n{markdown}";

        return HPD.TUI.Content.TextBlock.Create(body);
    }

    private static string EscapeMarkdown(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);
}
